using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>Cas d'utilisation LikeTrack, UnlikeTrack et consultation de l'état du like.</summary>
public sealed class LikeService(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    /// <summary>
    /// Ajoute un like. L'opération est idempotente : un second appel ne crée pas de doublon,
    /// la contrainte de clé primaire composite garantissant l'unicité en base.
    /// </summary>
    public async Task<LikeStateDto> LikeAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var track = await LoadAccessibleTrackAsync(trackId, cancellationToken);

        if (await db.TrackLikes.AnyAsync(l => l.TrackId == trackId && l.UserId == userId, cancellationToken))
        {
            return await BuildStateAsync(trackId, track.OwnerId, liked: true, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.TrackLikes.Add(new TrackLike { TrackId = trackId, UserId = userId, CreatedAt = clock.UtcNow });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Deux requêtes concurrentes du même utilisateur : le like existe déjà, rien à faire.
            await transaction.RollbackAsync(cancellationToken);
            return await BuildStateAsync(trackId, track.OwnerId, liked: true, cancellationToken);
        }

        // Incrément atomique côté SQL afin d'éviter toute perte de mise à jour concurrente.
        await db.Tracks
            .Where(t => t.Id == trackId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LikeCount, t => t.LikeCount + 1), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await BuildStateAsync(trackId, track.OwnerId, liked: true, cancellationToken);
    }

    /// <summary>Retire un like. L'opération est idempotente.</summary>
    public async Task<LikeStateDto> UnlikeAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var track = await LoadAccessibleTrackAsync(trackId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var removed = await db.TrackLikes
            .Where(l => l.TrackId == trackId && l.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            await db.Tracks
                .Where(t => t.Id == trackId && t.LikeCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LikeCount, t => t.LikeCount - 1), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await BuildStateAsync(trackId, track.OwnerId, liked: false, cancellationToken);
    }

    /// <summary>Retourne l'état du like de l'appelant sur un morceau.</summary>
    public async Task<LikeStateDto> GetStateAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await LoadAccessibleTrackAsync(trackId, cancellationToken);
        var liked = currentUser.UserId is { } userId
                    && await db.TrackLikes.AnyAsync(l => l.TrackId == trackId && l.UserId == userId, cancellationToken);

        return await BuildStateAsync(trackId, track.OwnerId, liked, cancellationToken);
    }

    /// <summary>Morceaux aimés par l'utilisateur connecté, du plus récent au plus ancien.</summary>
    public async Task<PagedResult<TrackDto>> ListLikedAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var query = db.TrackLikes
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Track.DeletedAt == null)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.Track);

        return await query.ToTrackPageAsync(page, userId, currentUser.Role, cancellationToken);
    }

    /// <summary>Construit l'état du like en respectant la visibilité du compteur.</summary>
    private async Task<LikeStateDto> BuildStateAsync(Guid trackId, Guid ownerId, bool liked, CancellationToken cancellationToken)
    {
        var stats = await db.Tracks
            .AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.LikeCount, t.Owner.Settings.ShowLikeCount })
            .FirstAsync(cancellationToken);

        var isPrivileged = currentUser.UserId == ownerId || currentUser.Role == UserRole.Admin;
        return new LikeStateDto(liked, isPrivileged || stats.ShowLikeCount ? stats.LikeCount : null);
    }

    /// <summary>Charge un morceau accessible à l'appelant, ou lève 404.</summary>
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
}
