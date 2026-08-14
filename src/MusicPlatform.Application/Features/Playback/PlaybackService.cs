using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Playback;

/// <summary>
/// Cas d'utilisation RegisterPlay, SavePlaybackProgress et historique d'écoute.
/// Le serveur reste seul juge de la validité d'une écoute : la déclaration du client
/// n'est jamais acceptée telle quelle.
/// </summary>
public sealed class PlaybackService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICacheService cache,
    IClock clock)
{
    /// <summary>Fenêtre pendant laquelle une même session ne peut pas recompter le même morceau.</summary>
    private static readonly TimeSpan PlayDeduplicationWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enregistre une écoute si elle est valide : au moins dix secondes réellement écoutées,
    /// une durée cohérente avec celle du morceau, et aucune écoute déjà comptée récemment
    /// pour le même couple (auditeur, morceau).
    /// </summary>
    public async Task<RegisterPlayResultDto> RegisterPlayAsync(Guid trackId, RegisterPlayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var track = await LoadAccessibleTrackAsync(trackId, cancellationToken);

        if (request.DurationSeconds < Track.MinimumValidPlaySeconds)
        {
            return await RejectAsync(track, "PLAY_TOO_SHORT", cancellationToken);
        }

        // Une durée écoutée supérieure à la durée du morceau indique une déclaration erronée.
        if (track.DurationSeconds > 0 && request.DurationSeconds > track.DurationSeconds + Track.MinimumValidPlaySeconds)
        {
            return await RejectAsync(track, "PLAY_DURATION_INCONSISTENT", cancellationToken);
        }

        var listenerKey = currentUser.UserId?.ToString() ?? request.SessionId?.ToString();
        if (listenerKey is null)
        {
            return await RejectAsync(track, "PLAY_LISTENER_UNKNOWN", cancellationToken);
        }

        if (!await cache.TryMarkAsync($"play:{trackId}:{listenerKey}", PlayDeduplicationWindow, cancellationToken))
        {
            return await RejectAsync(track, "PLAY_ALREADY_COUNTED", cancellationToken);
        }

        if (await HasRecentPlayAsync(trackId, request.SessionId, cancellationToken))
        {
            return await RejectAsync(track, "PLAY_ALREADY_COUNTED", cancellationToken);
        }

        var now = clock.UtcNow;
        db.PlayEvents.Add(new PlayEvent
        {
            TrackId = trackId,
            UserId = currentUser.UserId,
            SessionId = request.SessionId,
            PlayedAt = now,
            DurationSeconds = Math.Min(request.DurationSeconds, Math.Max(track.DurationSeconds, request.DurationSeconds)),
            Source = Truncate(request.Source, 32),
        });
        await db.SaveChangesAsync(cancellationToken);

        await db.Tracks
            .Where(t => t.Id == trackId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.PlayCount, t => t.PlayCount + 1), cancellationToken);

        await SaveProgressInternalAsync(trackId, request.PositionSeconds, track.DurationSeconds, now, cancellationToken);

        return new RegisterPlayResultDto(true, null, await ReadPlayCountAsync(track, cancellationToken));
    }

    /// <summary>Sauvegarde la position courante de lecture de l'utilisateur connecté.</summary>
    public async Task<PlaybackProgressDto> SaveProgressAsync(Guid trackId, SaveProgressRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        currentUser.RequireUserId();
        var track = await LoadAccessibleTrackAsync(trackId, cancellationToken);
        var now = clock.UtcNow;

        var position = await SaveProgressInternalAsync(trackId, request.PositionSeconds, track.DurationSeconds, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new PlaybackProgressDto(trackId, position, now);
    }

    /// <summary>Retourne la dernière position connue de l'utilisateur sur un morceau.</summary>
    public async Task<PlaybackProgressDto> GetProgressAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var entry = await db.ListeningHistories
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.TrackId == trackId)
            .Select(h => new { h.LastPositionSeconds, h.LastPlayedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return entry is null
            ? new PlaybackProgressDto(trackId, 0, null)
            : new PlaybackProgressDto(trackId, entry.LastPositionSeconds, entry.LastPlayedAt);
    }

    /// <summary>Historique d'écoute paginé de l'utilisateur connecté.</summary>
    public async Task<PagedResult<HistoryEntryDto>> GetHistoryAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var query = db.ListeningHistories
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.Track.DeletedAt == null)
            .OrderByDescending(h => h.LastPlayedAt);

        var total = await query.LongCountAsync(cancellationToken);
        if (total == 0)
        {
            return PagedResult<HistoryEntryDto>.Empty(page.Page, page.PageSize);
        }

        var rows = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(h => new { h.TrackId, h.LastPositionSeconds, h.LastPlayedAt })
            .ToListAsync(cancellationToken);

        // Deuxième requête unique pour les morceaux : évite une requête par entrée d'historique.
        var trackIds = rows.Select(r => r.TrackId).ToList();
        var tracks = await db.Tracks
            .AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .Project(userId)
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var items = new List<HistoryEntryDto>(rows.Count);
        foreach (var row in rows)
        {
            if (tracks.TryGetValue(row.TrackId, out var projection))
            {
                items.Add(new HistoryEntryDto(
                    projection.ToDto(userId, currentUser.Role),
                    row.LastPositionSeconds,
                    row.LastPlayedAt));
            }
        }

        return new PagedResult<HistoryEntryDto>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalItems = total,
        };
    }

    /// <summary>Efface l'intégralité de l'historique d'écoute de l'utilisateur connecté.</summary>
    public async Task ClearHistoryAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await db.ListeningHistories.Where(h => h.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Met à jour l'entrée d'historique sans persister : l'appelant contrôle le moment
    /// de la sauvegarde. Retourne la position effectivement retenue.
    /// </summary>
    private async Task<int> SaveProgressInternalAsync(
        Guid trackId,
        int requestedPosition,
        int trackDuration,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return 0;
        }

        var position = Math.Max(0, requestedPosition);
        if (trackDuration > 0)
        {
            position = Math.Min(position, trackDuration);
        }

        var entry = await db.ListeningHistories
            .FirstOrDefaultAsync(h => h.UserId == userId && h.TrackId == trackId, cancellationToken);

        if (entry is null)
        {
            entry = new ListeningHistory { UserId = userId, TrackId = trackId };
            db.ListeningHistories.Add(entry);
        }

        entry.LastPositionSeconds = position;
        entry.LastPlayedAt = now;
        return position;
    }

    /// <summary>Vérifie en base l'absence d'écoute récente, en repli du cache.</summary>
    private async Task<bool> HasRecentPlayAsync(Guid trackId, Guid? sessionId, CancellationToken cancellationToken)
    {
        var threshold = clock.UtcNow - PlayDeduplicationWindow;

        if (currentUser.UserId is { } userId)
        {
            return await db.PlayEvents.AnyAsync(
                p => p.TrackId == trackId && p.UserId == userId && p.PlayedAt >= threshold,
                cancellationToken);
        }

        return sessionId is not null
               && await db.PlayEvents.AnyAsync(
                   p => p.TrackId == trackId && p.SessionId == sessionId && p.PlayedAt >= threshold,
                   cancellationToken);
    }

    /// <summary>Construit un refus d'écoute avec le compteur courant.</summary>
    private async Task<RegisterPlayResultDto> RejectAsync(Track track, string reason, CancellationToken cancellationToken) =>
        new(false, reason, await ReadPlayCountAsync(track, cancellationToken));

    /// <summary>Lit le compteur d'écoutes en respectant la préférence de visibilité du propriétaire.</summary>
    private async Task<long?> ReadPlayCountAsync(Track track, CancellationToken cancellationToken)
    {
        var stats = await db.Tracks
            .AsNoTracking()
            .Where(t => t.Id == track.Id)
            .Select(t => new { t.PlayCount, t.Owner.Settings.ShowPlayCount })
            .FirstAsync(cancellationToken);

        var isPrivileged = currentUser.UserId == track.OwnerId || currentUser.Role == UserRole.Admin;
        return isPrivileged || stats.ShowPlayCount ? stats.PlayCount : null;
    }

    /// <summary>Charge un morceau écoutable par l'appelant.</summary>
    private async Task<Track> LoadAccessibleTrackAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == trackId && t.DeletedAt == null, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        if (!track.IsAccessibleBy(currentUser.UserId, currentUser.Role))
        {
            throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");
        }

        return track;
    }

    /// <summary>Tronque une chaîne libre à la longueur maximale stockée.</summary>
    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
