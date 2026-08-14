using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Playlists;

/// <summary>
/// Cas d'utilisation des playlists : CreatePlaylist, AddTrackToPlaylist, ReorderPlaylist,
/// duplication, partage, abonnement et mise en favori.
/// </summary>
public sealed class PlaylistService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    IClock clock)
{
    /// <summary>Taille d'un avatar de playlist en pixels.</summary>
    private const int PlaylistCoverEdgePixels = 500;

    /// <summary>Nombre de morceaux renvoyés avec le détail d'une playlist.</summary>
    private const int DetailTrackLimit = 200;

    /// <summary>Playlists visibles par l'appelant, éventuellement restreintes à un propriétaire.</summary>
    public async Task<PagedResult<PlaylistDto>> ListAsync(Guid? ownerId, PageRequest page, string? sort, CancellationToken cancellationToken)
    {
        var query = VisiblePlaylists();

        if (ownerId is not null)
        {
            query = query.Where(p => p.OwnerId == ownerId);
        }

        query = sort?.ToLowerInvariant() switch
        {
            "popular" => query.OrderByDescending(p => p.Follows.Count).ThenByDescending(p => p.UpdatedAt),
            "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
        };

        var projected = query.Project(currentUser.UserId);
        return await projected.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Playlists publiques d'un utilisateur identifié par son pseudo.</summary>
    public async Task<PagedResult<PlaylistDto>> ListByUsernameAsync(string username, PageRequest page, CancellationToken cancellationToken)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var owner = await db.Users
            .AsNoTracking()
            .Where(u => u.UsernameNormalized == normalized && u.DeletedAt == null)
            .Select(u => new { u.Id, u.ProfileVisibility })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");

        var isPrivileged = currentUser.UserId == owner.Id || currentUser.Role is UserRole.Moderator or UserRole.Admin;
        if (owner.ProfileVisibility == ProfileVisibility.Private && !isPrivileged)
        {
            return PagedResult<PlaylistDto>.Empty(page.Page, page.PageSize);
        }

        return await ListAsync(owner.Id, page, null, cancellationToken);
    }

    /// <summary>Playlists suivies ou mises en favori par l'utilisateur connecté.</summary>
    public async Task<PagedResult<PlaylistDto>> ListFavoritesAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var query = db.PlaylistFavorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Playlist)
            .Project(userId);

        return await query.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Détail d'une playlist avec ses morceaux ordonnés.</summary>
    public async Task<PlaylistDetailsDto> GetAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists
            .AsNoTracking()
            .Where(p => p.Id == playlistId)
            .Project(currentUser.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.PlaylistNotFound, "The requested playlist does not exist.");

        await EnsureVisibleAsync(playlistId, cancellationToken);

        var rows = await db.PlaylistItems
            .AsNoTracking()
            .Where(i => i.PlaylistId == playlistId && i.Track.DeletedAt == null)
            .OrderBy(i => i.Position)
            .Take(DetailTrackLimit)
            .Select(i => new { i.Position, i.AddedAt, i.TrackId })
            .ToListAsync(cancellationToken);

        var trackIds = rows.Select(r => r.TrackId).ToList();
        var tracks = await db.Tracks
            .AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .Project(currentUser.UserId)
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var items = new List<PlaylistTrackDto>(rows.Count);
        foreach (var row in rows)
        {
            if (tracks.TryGetValue(row.TrackId, out var projection))
            {
                items.Add(new PlaylistTrackDto(
                    projection.ToDto(currentUser.UserId, currentUser.Role),
                    row.Position,
                    row.AddedAt));
            }
        }

        var canEdit = currentUser.UserId == playlist.Owner.Id || currentUser.Role == UserRole.Admin;
        return new PlaylistDetailsDto(playlist, items, canEdit);
    }

    /// <summary>Crée une playlist appartenant à l'utilisateur connecté.</summary>
    public async Task<PlaylistDto> CreateAsync(CreatePlaylistRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var playlist = new Playlist
        {
            OwnerId = userId,
            Name = RequireName(request.Name),
            Description = request.Description?.Trim(),
            Visibility = request.Visibility,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);

        return await ReadDtoAsync(playlist.Id, cancellationToken);
    }

    /// <summary>Met à jour le nom, la description ou la visibilité d'une playlist.</summary>
    public async Task<PlaylistDto> UpdateAsync(Guid playlistId, UpdatePlaylistRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var playlist = await LoadForManagementAsync(playlistId, cancellationToken);

        if (request.Name is not null)
        {
            playlist.Name = RequireName(request.Name);
        }

        if (request.Description is not null)
        {
            playlist.Description = request.Description.Trim();
        }

        if (request.Visibility is { } visibility)
        {
            playlist.Visibility = visibility;
        }

        if (request.ClearCover && playlist.CoverFileId is { } fileId)
        {
            await RemoveCoverFileAsync(fileId, cancellationToken);
            playlist.CoverFileId = null;
        }

        playlist.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>Supprime une playlist et sa pochette.</summary>
    public async Task DeleteAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var playlist = await LoadForManagementAsync(playlistId, cancellationToken);
        var coverFileId = playlist.CoverFileId;

        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(cancellationToken);

        if (coverFileId is { } fileId)
        {
            await RemoveCoverFileAsync(fileId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Remplace la pochette d'une playlist.</summary>
    public async Task<PlaylistDto> SetCoverAsync(Guid playlistId, UploadedImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        var playlist = await LoadForManagementAsync(playlistId, cancellationToken);
        var previousFileId = playlist.CoverFileId;

        var resized = imageProcessor.CreateSquare(image.Bytes, PlaylistCoverEdgePixels);
        var file = new StoredFile { MimeType = "image/webp", Width = resized.Width, Height = resized.Height, CreatedAt = clock.UtcNow };
        file.StoragePath = StoragePaths.PlaylistCover(file.Id);

        using (var buffer = new MemoryStream(resized.Bytes, writable: false))
        {
            var written = await storage.SaveAsync(file.StoragePath, buffer, cancellationToken);
            file.FileSize = written.SizeBytes;
        }

        db.StoredFiles.Add(file);
        playlist.CoverFileId = file.Id;
        playlist.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (previousFileId is { } oldId)
        {
            await RemoveCoverFileAsync(oldId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>Ajoute un morceau à la fin d'une playlist.</summary>
    public async Task<PlaylistDto> AddTrackAsync(Guid playlistId, Guid trackId, CancellationToken cancellationToken)
    {
        var playlist = await LoadForManagementAsync(playlistId, cancellationToken);

        var track = await db.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == trackId && t.DeletedAt == null, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        if (!track.IsAccessibleBy(currentUser.UserId, currentUser.Role))
        {
            throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");
        }

        if (await db.PlaylistItems.AnyAsync(i => i.PlaylistId == playlistId && i.TrackId == trackId, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.PlaylistTrackAlreadyPresent, "This track is already in the playlist.");
        }

        var count = await db.PlaylistItems.CountAsync(i => i.PlaylistId == playlistId, cancellationToken);
        if (count >= Playlist.MaxItems)
        {
            throw new ConflictException(ErrorCodes.PlaylistFull, $"A playlist cannot contain more than {Playlist.MaxItems} tracks.");
        }

        db.PlaylistItems.Add(new PlaylistItem
        {
            PlaylistId = playlistId,
            TrackId = trackId,
            Position = count,
            AddedAt = clock.UtcNow,
        });

        playlist.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>
    /// Retire un morceau d'une playlist et compacte les positions restantes,
    /// afin qu'elles forment toujours une suite contiguë à partir de zéro.
    /// </summary>
    public async Task<PlaylistDto> RemoveTrackAsync(Guid playlistId, Guid trackId, CancellationToken cancellationToken)
    {
        var playlist = await LoadForManagementAsync(playlistId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var items = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .OrderBy(i => i.Position)
            .ToListAsync(cancellationToken);

        var target = items.FirstOrDefault(i => i.TrackId == trackId)
            ?? throw new NotFoundException(ErrorCodes.PlaylistTrackNotPresent, "This track is not in the playlist.");

        db.PlaylistItems.Remove(target);
        items.Remove(target);

        for (var index = 0; index < items.Count; index++)
        {
            items[index].Position = index;
        }

        playlist.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>
    /// Applique un nouvel ordre complet à la playlist. La validation des positions est
    /// portée par le domaine ; l'écriture est transactionnelle.
    /// </summary>
    public async Task<PlaylistDto> ReorderAsync(Guid playlistId, ReorderPlaylistRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new InputValidationException("items", "The reorder request must contain at least one item.");
        }

        var playlist = await LoadForManagementAsync(playlistId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        playlist.Items = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .ToListAsync(cancellationToken);

        var positions = new Dictionary<Guid, int>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (!positions.TryAdd(item.TrackId, item.Position))
            {
                throw new InputValidationException("items", "A track appears more than once in the reorder request.");
            }
        }

        playlist.Reorder(positions, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>Duplique une playlist visible vers le compte de l'utilisateur connecté.</summary>
    public async Task<PlaylistDto> DuplicateAsync(Guid playlistId, DuplicatePlaylistRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var source = await EnsureVisibleAsync(playlistId, cancellationToken);
        var now = clock.UtcNow;

        var copy = new Playlist
        {
            OwnerId = userId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} (copy)" : request.Name.Trim(),
            Description = source.Description,
            Visibility = request.Visibility ?? ContentVisibility.Private,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Playlists.Add(copy);

        var items = await db.PlaylistItems
            .AsNoTracking()
            .Where(i => i.PlaylistId == playlistId && i.Track.DeletedAt == null)
            .OrderBy(i => i.Position)
            .Select(i => i.TrackId)
            .Take(Playlist.MaxItems)
            .ToListAsync(cancellationToken);

        for (var index = 0; index < items.Count; index++)
        {
            db.PlaylistItems.Add(new PlaylistItem
            {
                PlaylistId = copy.Id,
                TrackId = items[index],
                Position = index,
                AddedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ReadDtoAsync(copy.Id, cancellationToken);
    }

    /// <summary>Suit une playlist visible. L'opération est idempotente.</summary>
    public async Task<PlaylistDto> FollowAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await EnsureVisibleAsync(playlistId, cancellationToken);

        if (!await db.PlaylistFollows.AnyAsync(f => f.PlaylistId == playlistId && f.UserId == userId, cancellationToken))
        {
            db.PlaylistFollows.Add(new PlaylistFollow { PlaylistId = playlistId, UserId = userId, CreatedAt = clock.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>Cesse de suivre une playlist. L'opération est idempotente.</summary>
    public async Task<PlaylistDto> UnfollowAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await EnsureVisibleAsync(playlistId, cancellationToken);

        await db.PlaylistFollows
            .Where(f => f.PlaylistId == playlistId && f.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>Ajoute une playlist aux favoris. L'opération est idempotente.</summary>
    public async Task<PlaylistDto> FavoriteAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await EnsureVisibleAsync(playlistId, cancellationToken);

        if (!await db.PlaylistFavorites.AnyAsync(f => f.PlaylistId == playlistId && f.UserId == userId, cancellationToken))
        {
            db.PlaylistFavorites.Add(new PlaylistFavorite { PlaylistId = playlistId, UserId = userId, CreatedAt = clock.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>Retire une playlist des favoris. L'opération est idempotente.</summary>
    public async Task<PlaylistDto> UnfavoriteAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await EnsureVisibleAsync(playlistId, cancellationToken);

        await db.PlaylistFavorites
            .Where(f => f.PlaylistId == playlistId && f.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return await ReadDtoAsync(playlistId, cancellationToken);
    }

    /// <summary>Playlists visibles par l'appelant : publiques, non répertoriées et les siennes.</summary>
    private IQueryable<Playlist> VisiblePlaylists()
    {
        var query = db.Playlists.AsNoTracking();

        if (currentUser.Role is UserRole.Moderator or UserRole.Admin)
        {
            return query;
        }

        var viewerId = currentUser.UserId;
        return viewerId is null
            ? query.Where(p => p.Visibility == ContentVisibility.Public)
            : query.Where(p => p.OwnerId == viewerId || p.Visibility == ContentVisibility.Public);
    }

    /// <summary>
    /// Charge une playlist modifiable par l'appelant.
    ///
    /// Une playlist que l'appelant ne peut pas voir renvoie 404 et non 403 : répondre
    /// « interdit » révélerait l'existence d'une ressource privée.
    /// </summary>
    private async Task<Playlist> LoadForManagementAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.PlaylistNotFound, "The requested playlist does not exist.");

        if (!playlist.IsAccessibleBy(currentUser.UserId, currentUser.Role))
        {
            throw new NotFoundException(ErrorCodes.PlaylistNotFound, "The requested playlist does not exist.");
        }

        if (!playlist.IsManageableBy(currentUser.UserId, currentUser.Role))
        {
            throw new ForbiddenException("You are not allowed to modify this playlist.", ErrorCodes.PlaylistAccessDenied);
        }

        return playlist;
    }

    /// <summary>Vérifie qu'une playlist est consultable par l'appelant.</summary>
    private async Task<Playlist> EnsureVisibleAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playlistId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.PlaylistNotFound, "The requested playlist does not exist.");

        if (!playlist.IsAccessibleBy(currentUser.UserId, currentUser.Role))
        {
            throw new NotFoundException(ErrorCodes.PlaylistNotFound, "The requested playlist does not exist.");
        }

        return playlist;
    }

    /// <summary>Relit une playlist sous sa forme DTO après modification.</summary>
    private async Task<PlaylistDto> ReadDtoAsync(Guid playlistId, CancellationToken cancellationToken) =>
        await db.Playlists
            .AsNoTracking()
            .Where(p => p.Id == playlistId)
            .Project(currentUser.UserId)
            .FirstAsync(cancellationToken);

    /// <summary>Supprime le fichier de pochette et son enregistrement.</summary>
    private async Task RemoveCoverFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null)
        {
            return;
        }

        db.StoredFiles.Remove(file);
        await storage.DeleteAsync(file.StoragePath, cancellationToken);
    }

    /// <summary>Valide le nom d'une playlist.</summary>
    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new InputValidationException("name", "A playlist name is required.");
        }

        if (trimmed.Length > 120)
        {
            throw new InputValidationException("name", "A playlist name cannot exceed 120 characters.");
        }

        return trimmed;
    }
}

/// <summary>Projection des playlists vers leur DTO, en une seule requête SQL.</summary>
public static class PlaylistQueries
{
    /// <summary>Projette les playlists avec leurs compteurs et l'état de l'appelant.</summary>
    public static IQueryable<PlaylistDto> Project(this IQueryable<Playlist> query, Guid? viewerId) =>
        query.Select(p => new PlaylistDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Visibility = p.Visibility,
            CoverUrl = p.CoverFileId == null ? null : MediaUrls.Base + "/media/playlist-covers/" + p.CoverFileId,
            Owner = new UserRefDto(
                p.OwnerId,
                p.Owner.Username,
                p.Owner.AvatarFileId == null ? null : MediaUrls.Base + "/media/avatars/" + p.Owner.AvatarFileId),
            TrackCount = p.Items.Count(i => i.Track.DeletedAt == null),
            TotalDurationSeconds = p.Items.Where(i => i.Track.DeletedAt == null).Sum(i => i.Track.DurationSeconds),
            FollowerCount = p.Follows.Count,
            IsFollowedByCurrentUser = viewerId == null ? null : p.Follows.Any(f => f.UserId == viewerId),
            IsFavoritedByCurrentUser = viewerId == null ? null : p.Favorites.Any(f => f.UserId == viewerId),
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
        });
}
