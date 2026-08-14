using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Analytics;

/// <summary>
/// Tableau de bord de l'artiste. Toutes les requêtes sont bornées au propriétaire connecté :
/// aucun utilisateur ne peut consulter les statistiques d'un autre.
/// </summary>
public sealed class AnalyticsService(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    /// <summary>Amplitude maximale d'une série temporelle, en jours.</summary>
    private const int MaxRangeDays = 366;

    /// <summary>Période par défaut lorsqu'aucune borne n'est fournie.</summary>
    private const int DefaultRangeDays = 30;

    /// <summary>Chiffres clés du compte connecté.</summary>
    public async Task<AnalyticsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var since = clock.UtcNow.AddDays(-DefaultRangeDays);

        var tracks = await db.Tracks
            .AsNoTracking()
            .Where(t => t.OwnerId == userId && t.DeletedAt == null)
            .GroupBy(t => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Publics = g.Count(t => t.Visibility == ContentVisibility.Public && t.Status == TrackStatus.Ready),
                Plays = g.Sum(t => t.PlayCount),
                Likes = g.Sum(t => t.LikeCount),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var followerCount = await db.Follows.CountAsync(f => f.FollowedId == userId, cancellationToken);

        var commentCount = await db.Comments
            .CountAsync(c => c.DeletedAt == null && c.Track.OwnerId == userId && c.Track.DeletedAt == null, cancellationToken);

        var recentPlays = await db.PlayEvents
            .LongCountAsync(p => p.Track.OwnerId == userId && p.PlayedAt >= since, cancellationToken);

        return new AnalyticsOverviewDto
        {
            TrackCount = tracks?.Total ?? 0,
            PublicTrackCount = tracks?.Publics ?? 0,
            TotalPlays = tracks?.Plays ?? 0,
            TotalLikes = tracks?.Likes ?? 0,
            FollowerCount = followerCount,
            CommentCount = commentCount,
            PlaysLast30Days = recentPlays,
        };
    }

    /// <summary>Statistiques détaillées, morceau par morceau.</summary>
    public async Task<PagedResult<TrackAnalyticsDto>> GetTrackAnalyticsAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var query = db.Tracks
            .AsNoTracking()
            .Where(t => t.OwnerId == userId && t.DeletedAt == null)
            .OrderByDescending(t => t.PlayCount)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new TrackAnalyticsDto
            {
                TrackId = t.Id,
                Title = t.Title,
                PlayCount = t.PlayCount,
                LikeCount = t.LikeCount,
                CommentCount = t.Comments.Count(c => c.DeletedAt == null),
                PlaylistCount = t.PlaylistItems.Count,
                Visibility = t.Visibility,
                CreatedAt = t.CreatedAt,
            });

        return await query.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Morceaux les plus écoutés du compte connecté.</summary>
    public async Task<IReadOnlyList<TrackAnalyticsDto>> GetTopTracksAsync(int limit, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var count = Math.Clamp(limit, 1, 50);

        return await db.Tracks
            .AsNoTracking()
            .Where(t => t.OwnerId == userId && t.DeletedAt == null)
            .OrderByDescending(t => t.PlayCount)
            .ThenBy(t => t.Id)
            .Take(count)
            .Select(t => new TrackAnalyticsDto
            {
                TrackId = t.Id,
                Title = t.Title,
                PlayCount = t.PlayCount,
                LikeCount = t.LikeCount,
                CommentCount = t.Comments.Count(c => c.DeletedAt == null),
                PlaylistCount = t.PlaylistItems.Count,
                Visibility = t.Visibility,
                CreatedAt = t.CreatedAt,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Série temporelle des écoutes. L'agrégation est faite en base par jour, puis
    /// regroupée par semaine ou par mois en mémoire sur un volume déjà borné.
    /// </summary>
    public async Task<PlaysSeriesDto> GetPlaysSeriesAsync(DateTime? from, DateTime? to, AnalyticsGroupBy groupBy, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var (start, end) = NormalizeRange(from, to);

        var daily = await QueryDailyPlaysAsync(db.PlayEvents.Where(p => p.Track.OwnerId == userId), start, end, cancellationToken);
        return new PlaysSeriesDto(start, end, groupBy, GroupPoints(daily, groupBy));
    }

    /// <summary>Agrège les écoutes par jour sur une plage bornée.</summary>
    internal static async Task<List<PlaysPointDto>> QueryDailyPlaysAsync(
        IQueryable<Domain.Entities.PlayEvent> source,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken)
    {
        var rows = await source
            .AsNoTracking()
            .Where(p => p.PlayedAt >= start && p.PlayedAt < end)
            .GroupBy(p => p.PlayedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Plays = g.LongCount(),
                Listeners = g.Select(p => p.UserId).Distinct().Count(),
            })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new PlaysPointDto(DateOnly.FromDateTime(r.Date), r.Plays, r.Listeners))
            .ToList();
    }

    /// <summary>Regroupe des points journaliers en semaines ou en mois.</summary>
    internal static IReadOnlyList<PlaysPointDto> GroupPoints(IReadOnlyList<PlaysPointDto> daily, AnalyticsGroupBy groupBy)
    {
        if (groupBy == AnalyticsGroupBy.Day || daily.Count == 0)
        {
            return daily;
        }

        var buckets = new Dictionary<DateOnly, (long Plays, int Listeners)>();
        var order = new List<DateOnly>();

        foreach (var point in daily)
        {
            var key = groupBy == AnalyticsGroupBy.Week ? StartOfWeek(point.Date) : new DateOnly(point.Date.Year, point.Date.Month, 1);

            if (buckets.TryGetValue(key, out var current))
            {
                buckets[key] = (current.Plays + point.Plays, current.Listeners + point.UniqueListeners);
            }
            else
            {
                buckets[key] = (point.Plays, point.UniqueListeners);
                order.Add(key);
            }
        }

        order.Sort();
        return order.Select(key => new PlaysPointDto(key, buckets[key].Plays, buckets[key].Listeners)).ToList();
    }

    /// <summary>Ramène une date au lundi de sa semaine.</summary>
    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    /// <summary>Normalise et borne la plage demandée afin d'éviter les requêtes non maîtrisées.</summary>
    internal static (DateTime Start, DateTime End) NormalizeRange(DateTime? from, DateTime? to)
    {
        var end = (to ?? DateTime.UtcNow).ToUniversalTime().Date.AddDays(1);
        var start = (from ?? end.AddDays(-DefaultRangeDays)).ToUniversalTime().Date;

        if (start >= end)
        {
            throw new InputValidationException("from", "The start of the range must be before its end.");
        }

        if ((end - start).TotalDays > MaxRangeDays)
        {
            throw new InputValidationException("from", $"The range cannot exceed {MaxRangeDays} days.");
        }

        return (start, end);
    }
}
