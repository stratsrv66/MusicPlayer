using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Application.Features.Users;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Application.Features.Discovery;

/// <summary>
/// Moteur de recommandation déterministe et explicable, sans apprentissage automatique.
///
/// Le score d'un morceau candidat est la somme de bonus indépendants :
/// <list type="bullet">
///   <item><description>+40 si son auteur fait partie des artistes suivis ;</description></item>
///   <item><description>+25 si son genre figure parmi les genres les plus écoutés ou aimés ;</description></item>
///   <item><description>+15 par tag partagé avec les morceaux aimés, plafonné à 30 ;</description></item>
///   <item><description>jusqu'à +20 selon la popularité récente, sur une échelle logarithmique ;</description></item>
///   <item><description>jusqu'à +15 selon la fraîcheur de la publication.</description></item>
/// </list>
/// Les morceaux déjà écoutés ou aimés sont exclus, et le classement final est stable.
/// </summary>
public sealed class RecommendationService(IAppDbContext db, ICurrentUser currentUser, ICacheService cache, IClock clock)
{
    private const int FollowedArtistBonus = 40;
    private const int PreferredGenreBonus = 25;
    private const int SharedTagBonus = 15;
    private const int MaxSharedTagBonus = 30;
    private const int MaxPopularityBonus = 20;
    private const int MaxFreshnessBonus = 15;

    /// <summary>Nombre de morceaux candidats analysés avant classement.</summary>
    private const int CandidatePoolSize = 200;

    /// <summary>Durée de mise en cache des recommandations d'un utilisateur.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>Fenêtre considérée comme « récente » pour le bonus de fraîcheur.</summary>
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Retourne des morceaux recommandés. Pour un visiteur anonyme, le moteur se replie
    /// sur la popularité récente, qui reste un signal pertinent et explicable.
    /// </summary>
    public async Task<IReadOnlyList<TrackDto>> GetTrackRecommendationsAsync(int limit, CancellationToken cancellationToken)
    {
        var count = Math.Clamp(limit, 1, 50);

        if (currentUser.UserId is not { } userId)
        {
            return await GetPopularAsync(count, cancellationToken);
        }

        var cacheKey = $"reco:tracks:{userId}:{count}";
        var cached = await cache.GetAsync<List<Guid>>(cacheKey, cancellationToken);
        if (cached is { Count: > 0 })
        {
            return await LoadOrderedAsync(cached, cancellationToken);
        }

        var profile = await BuildTasteProfileAsync(userId, cancellationToken);
        var candidates = await LoadCandidatesAsync(userId, profile, cancellationToken);

        var ranked = candidates
            .Select(candidate => new { candidate.Id, Score = Score(candidate, profile, clock.UtcNow) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .Take(count)
            .Select(x => x.Id)
            .ToList();

        if (ranked.Count < count)
        {
            // Le profil est trop pauvre pour remplir la liste : on complète par la popularité.
            var fillers = await GetPopularAsync(count, cancellationToken);
            var missing = fillers.Select(t => t.Id).Where(id => !ranked.Contains(id)).Take(count - ranked.Count);
            ranked.AddRange(missing);
        }

        await cache.SetAsync(cacheKey, ranked, CacheTtl, cancellationToken);
        return await LoadOrderedAsync(ranked, cancellationToken);
    }

    /// <summary>
    /// Retourne des artistes recommandés : ceux qui publient dans les genres écoutés,
    /// ordonnés par popularité, en excluant les artistes déjà suivis.
    /// </summary>
    public async Task<IReadOnlyList<UserSummaryDto>> GetArtistRecommendationsAsync(int limit, CancellationToken cancellationToken)
    {
        var count = Math.Clamp(limit, 1, 50);
        var userId = currentUser.UserId;

        var query = db.Users.AsNoTracking().Active()
            .Where(u => u.Tracks.Any(t => t.DeletedAt == null
                                          && t.HiddenAt == null
                                          && t.Visibility == Domain.Enums.ContentVisibility.Public
                                          && t.Status == Domain.Enums.TrackStatus.Ready));

        if (userId is { } id)
        {
            var profile = await BuildTasteProfileAsync(id, cancellationToken);
            query = query.Where(u => u.Id != id && !u.Followers.Any(f => f.FollowerId == id));

            if (profile.GenreIds.Count > 0)
            {
                query = query.Where(u => u.Tracks.Any(t => t.GenreId != null && profile.GenreIds.Contains(t.GenreId.Value)));
            }
        }

        return await query
            .OrderByDescending(u => u.Followers.Count)
            .ThenByDescending(u => u.Tracks.Sum(t => t.PlayCount))
            .ThenBy(u => u.Id)
            .Take(count)
            .ProjectSummary()
            .ToListAsync(cancellationToken);
    }

    /// <summary>Morceaux publics les plus écoutés récemment.</summary>
    public async Task<IReadOnlyList<TrackDto>> GetPopularAsync(int limit, CancellationToken cancellationToken)
    {
        var count = Math.Clamp(limit, 1, 50);
        var projections = await db.Tracks
            .AsNoTracking()
            .PubliclyListed()
            .OrderByDescending(t => t.PlayCount)
            .ThenByDescending(t => t.LikeCount)
            .ThenBy(t => t.Id)
            .Take(count)
            .Project(currentUser.UserId)
            .ToListAsync(cancellationToken);

        return projections.Select(p => p.ToDto(currentUser.UserId, currentUser.Role)).ToList();
    }

    /// <summary>Construit le profil de goûts à partir de l'historique, des likes et des abonnements.</summary>
    private async Task<TasteProfile> BuildTasteProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var followedArtists = await db.Follows
            .AsNoTracking()
            .Where(f => f.FollowerId == userId)
            .Select(f => f.FollowedId)
            .ToListAsync(cancellationToken);

        var likedTrackIds = await db.TrackLikes
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.TrackId)
            .Take(100)
            .ToListAsync(cancellationToken);

        var playedTrackIds = await db.ListeningHistories
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.LastPlayedAt)
            .Select(h => h.TrackId)
            .Take(100)
            .ToListAsync(cancellationToken);

        var seedIds = likedTrackIds.Concat(playedTrackIds).Distinct().ToList();

        var genreIds = seedIds.Count == 0
            ? []
            : await db.Tracks
                .AsNoTracking()
                .Where(t => seedIds.Contains(t.Id) && t.GenreId != null)
                .GroupBy(t => t.GenreId!.Value)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(5)
                .ToListAsync(cancellationToken);

        var tagIds = likedTrackIds.Count == 0
            ? []
            : await db.TrackTags
                .AsNoTracking()
                .Where(tt => likedTrackIds.Contains(tt.TrackId))
                .GroupBy(tt => tt.TagId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(15)
                .ToListAsync(cancellationToken);

        return new TasteProfile(
            followedArtists.ToHashSet(),
            genreIds.ToHashSet(),
            tagIds.ToHashSet(),
            seedIds.ToHashSet());
    }

    /// <summary>Charge un vivier de candidats pertinents, borné en taille.</summary>
    private async Task<List<Candidate>> LoadCandidatesAsync(Guid userId, TasteProfile profile, CancellationToken cancellationToken)
    {
        var query = db.Tracks
            .AsNoTracking()
            .PubliclyListed()
            .Where(t => t.OwnerId != userId && !profile.SeenTrackIds.Contains(t.Id));

        if (profile.HasSignals)
        {
            query = query.Where(t => profile.FollowedArtistIds.Contains(t.OwnerId)
                                     || (t.GenreId != null && profile.GenreIds.Contains(t.GenreId.Value))
                                     || t.TrackTags.Any(tt => profile.TagIds.Contains(tt.TagId)));
        }

        return await query
            .OrderByDescending(t => t.PublishedAt ?? t.CreatedAt)
            .Take(CandidatePoolSize)
            .Select(t => new Candidate
            {
                Id = t.Id,
                OwnerId = t.OwnerId,
                GenreId = t.GenreId,
                TagIds = t.TrackTags.Select(tt => tt.TagId).ToList(),
                PlayCount = t.PlayCount,
                LikeCount = t.LikeCount,
                PublishedAt = t.PublishedAt ?? t.CreatedAt,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Calcule le score d'un candidat selon les règles documentées sur la classe.</summary>
    private static int Score(Candidate candidate, TasteProfile profile, DateTime now)
    {
        var score = 0;

        if (profile.FollowedArtistIds.Contains(candidate.OwnerId))
        {
            score += FollowedArtistBonus;
        }

        if (candidate.GenreId is { } genreId && profile.GenreIds.Contains(genreId))
        {
            score += PreferredGenreBonus;
        }

        var sharedTags = 0;
        foreach (var tagId in candidate.TagIds)
        {
            if (profile.TagIds.Contains(tagId))
            {
                sharedTags++;
            }
        }

        score += Math.Min(sharedTags * SharedTagBonus, MaxSharedTagBonus);
        score += PopularityBonus(candidate.PlayCount + (candidate.LikeCount * 3));
        score += FreshnessBonus(candidate.PublishedAt, now);

        return score;
    }

    /// <summary>Échelle logarithmique : un morceau très écouté ne doit pas écraser les autres signaux.</summary>
    private static int PopularityBonus(long weightedPlays)
    {
        if (weightedPlays <= 0)
        {
            return 0;
        }

        var bonus = (int)Math.Round(Math.Log10(weightedPlays + 1) * 8);
        return Math.Min(bonus, MaxPopularityBonus);
    }

    /// <summary>Bonus décroissant linéairement sur la fenêtre de fraîcheur.</summary>
    private static int FreshnessBonus(DateTime publishedAt, DateTime now)
    {
        var age = now - publishedAt;
        if (age < TimeSpan.Zero || age > FreshnessWindow)
        {
            return 0;
        }

        var ratio = 1 - (age.TotalDays / FreshnessWindow.TotalDays);
        return (int)Math.Round(ratio * MaxFreshnessBonus);
    }

    /// <summary>Charge les morceaux d'une liste d'identifiants en préservant l'ordre fourni.</summary>
    private async Task<IReadOnlyList<TrackDto>> LoadOrderedAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var idList = ids.ToList();
        var projections = await db.Tracks
            .AsNoTracking()
            .PubliclyListed()
            .Where(t => idList.Contains(t.Id))
            .Project(currentUser.UserId)
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var ordered = new List<TrackDto>(ids.Count);
        foreach (var id in ids)
        {
            if (projections.TryGetValue(id, out var projection))
            {
                ordered.Add(projection.ToDto(currentUser.UserId, currentUser.Role));
            }
        }

        return ordered;
    }

    /// <summary>Signaux de goût extraits de l'activité de l'utilisateur.</summary>
    private sealed record TasteProfile(
        HashSet<Guid> FollowedArtistIds,
        HashSet<Guid> GenreIds,
        HashSet<Guid> TagIds,
        HashSet<Guid> SeenTrackIds)
    {
        /// <summary>Vrai si le profil contient au moins un signal exploitable.</summary>
        public bool HasSignals => FollowedArtistIds.Count > 0 || GenreIds.Count > 0 || TagIds.Count > 0;
    }

    /// <summary>Morceau candidat, chargé avec les seules colonnes nécessaires au scoring.</summary>
    private sealed class Candidate
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        public Guid? GenreId { get; init; }
        public List<Guid> TagIds { get; init; } = [];
        public long PlayCount { get; init; }
        public long LikeCount { get; init; }
        public DateTime PublishedAt { get; init; }
    }
}
