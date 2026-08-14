using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Account;
using MusicPlatform.Application.Features.Analytics;
using MusicPlatform.Application.Features.Moderation;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Admin;

/// <summary>
/// Cas d'utilisation d'administration. Chaque méthode vérifie explicitement le privilège
/// requis : la protection ne repose pas uniquement sur les attributs du contrôleur.
/// </summary>
public sealed class AdminService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    AuditLogger audit,
    IClock clock)
{
    /// <summary>Liste paginée des utilisateurs, avec recherche sur le pseudo et l'email.</summary>
    public async Task<PagedResult<AdminUserDto>> ListUsersAsync(string? query, UserRole? role, UserStatus? status, PageRequest page, CancellationToken cancellationToken)
    {
        EnsureAdmin();

        var users = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = SqlPatterns.Contains(query);
            users = users.Where(u => EF.Functions.Like(u.UsernameNormalized, pattern, SqlPatterns.EscapeCharacter) || EF.Functions.Like(u.Email, pattern, SqlPatterns.EscapeCharacter));
        }

        if (role is { } r)
        {
            users = users.Where(u => u.Role == r);
        }

        if (status is { } s)
        {
            users = users.Where(u => u.Status == s);
        }

        return await users
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Select(u => ProjectUser(u))
            .ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Détail administratif d'un utilisateur.</summary>
    public async Task<AdminUserDto> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        EnsureAdmin();

        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => ProjectUser(u))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");
    }

    /// <summary>
    /// Modifie le rôle ou le statut d'un compte. Un administrateur ne peut pas se
    /// rétrograder ni se suspendre lui-même, afin d'éviter la perte d'accès à la plateforme.
    /// </summary>
    public async Task<AdminUserDto> UpdateUserAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAdmin();

        var actorId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");

        if (user.Id == actorId && (request.Role is not null and not UserRole.Admin || request.Status == UserStatus.Suspended))
        {
            throw new ConflictException(ErrorCodes.Conflict, "An administrator cannot revoke their own access.");
        }

        if (request.Role is { } role)
        {
            user.Role = role;
        }

        if (request.Status is { } status)
        {
            user.Status = status;

            if (status == UserStatus.Suspended)
            {
                // Une suspension doit couper les sessions actives immédiatement.
                await db.RefreshTokens
                    .Where(t => t.UserId == userId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow), cancellationToken);
            }
        }

        user.UpdatedAt = clock.UtcNow;

        await audit.RecordAsync("USER_UPDATED", nameof(User), userId,
            new { role = request.Role?.ToString(), status = request.Status?.ToString() }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetUserAsync(userId, cancellationToken);
    }

    /// <summary>Liste globale des morceaux, y compris masqués et supprimés.</summary>
    public async Task<PagedResult<AdminTrackDto>> ListTracksAsync(string? query, bool includeDeleted, PageRequest page, CancellationToken cancellationToken)
    {
        EnsureCanModerate();

        var tracks = db.Tracks.AsNoTracking().AsQueryable();

        if (!includeDeleted)
        {
            tracks = tracks.Where(t => t.DeletedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = SqlPatterns.Contains(query);
            tracks = tracks.Where(t => EF.Functions.Like(t.Title.ToLower(), pattern, SqlPatterns.EscapeCharacter)
                                       || EF.Functions.Like(t.ArtistName.ToLower(), pattern, SqlPatterns.EscapeCharacter)
                                       || EF.Functions.Like(t.Owner.UsernameNormalized, pattern, SqlPatterns.EscapeCharacter));
        }

        return await tracks
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(t => new AdminTrackDto
            {
                Id = t.Id,
                Title = t.Title,
                ArtistName = t.ArtistName,
                Owner = new UserRefDto(
                    t.OwnerId,
                    t.Owner.Username,
                    t.Owner.AvatarFileId == null ? null : MediaUrls.Base + "/media/avatars/" + t.Owner.AvatarFileId),
                Visibility = t.Visibility,
                Status = t.Status,
                PlayCount = t.PlayCount,
                LikeCount = t.LikeCount,
                IsHidden = t.HiddenAt != null,
                IsDeleted = t.DeletedAt != null,
                CreatedAt = t.CreatedAt,
            })
            .ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Masque un morceau sans le supprimer.</summary>
    public async Task HideTrackAsync(Guid trackId, CancellationToken cancellationToken)
    {
        EnsureCanModerate();
        await SetTrackHiddenAsync(trackId, clock.UtcNow, "TRACK_HIDDEN", cancellationToken);
    }

    /// <summary>Restaure un morceau précédemment masqué.</summary>
    public async Task RestoreTrackAsync(Guid trackId, CancellationToken cancellationToken)
    {
        EnsureCanModerate();
        await SetTrackHiddenAsync(trackId, null, "TRACK_RESTORED", cancellationToken);
    }

    /// <summary>Supprime définitivement un morceau et ses fichiers.</summary>
    public async Task DeleteTrackAsync(Guid trackId, CancellationToken cancellationToken)
    {
        EnsureAdmin();

        var track = await db.Tracks
            .Include(t => t.File)
            .Include(t => t.Covers)
            .FirstOrDefaultAsync(t => t.Id == trackId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        var paths = new List<string>(track.Covers.Count + 1);
        if (track.File is not null)
        {
            paths.Add(track.File.StoragePath);
        }

        foreach (var cover in track.Covers)
        {
            paths.Add(cover.StoragePath);
        }

        db.Tracks.Remove(track);
        await audit.RecordAsync("TRACK_DELETED", nameof(Track), trackId, new { track.Title, track.OwnerId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var path in paths)
        {
            await storage.DeleteAsync(path, cancellationToken);
        }
    }

    /// <summary>Supprime administrativement un compte et l'ensemble de ses contenus.</summary>
    public async Task DeleteUserAsync(Guid userId, AccountService accountService, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountService);
        EnsureAdmin();

        if (userId == currentUser.UserId)
        {
            throw new ConflictException(ErrorCodes.Conflict, "Use the account deletion endpoint to delete your own account.");
        }

        await accountService.PurgeUserAsync(userId, cancellationToken);
        await audit.RecordAsync("USER_DELETED", nameof(User), userId, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Journal d'audit paginé.</summary>
    public async Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(string? action, PageRequest page, CancellationToken cancellationToken)
    {
        EnsureAdmin();

        var logs = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var pattern = SqlPatterns.Contains(action);
            logs = logs.Where(l => EF.Functions.Like(l.Action.ToLower(), pattern, SqlPatterns.EscapeCharacter));
        }

        return await logs
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                Actor = l.Actor == null
                    ? null
                    : new UserRefDto(
                        l.Actor.Id,
                        l.Actor.Username,
                        l.Actor.AvatarFileId == null ? null : MediaUrls.Base + "/media/avatars/" + l.Actor.AvatarFileId),
                Action = l.Action,
                TargetType = l.TargetType,
                TargetId = l.TargetId,
                Metadata = l.Metadata,
                CreatedAt = l.CreatedAt,
            })
            .ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Statistiques globales de la plateforme.</summary>
    public async Task<AdminStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        EnsureAdmin();

        var users = await db.Users
            .AsNoTracking()
            .GroupBy(u => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(u => u.DeletedAt == null && u.Status == UserStatus.Active),
                Suspended = g.Count(u => u.Status == UserStatus.Suspended),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var tracks = await db.Tracks
            .AsNoTracking()
            .Where(t => t.DeletedAt == null)
            .GroupBy(t => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Publics = g.Count(t => t.Visibility == ContentVisibility.Public && t.Status == TrackStatus.Ready),
                Hidden = g.Count(t => t.HiddenAt != null),
                Plays = g.Sum(t => t.PlayCount),
                Likes = g.Sum(t => t.LikeCount),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var (start, end) = AnalyticsService.NormalizeRange(null, null);
        var series = await AnalyticsService.QueryDailyPlaysAsync(db.PlayEvents, start, end, cancellationToken);

        return new AdminStatisticsDto
        {
            TotalUsers = users?.Total ?? 0,
            ActiveUsers = users?.Active ?? 0,
            SuspendedUsers = users?.Suspended ?? 0,
            TotalTracks = tracks?.Total ?? 0,
            PublicTracks = tracks?.Publics ?? 0,
            HiddenTracks = tracks?.Hidden ?? 0,
            TotalPlaylists = await db.Playlists.CountAsync(cancellationToken),
            TotalComments = await db.Comments.CountAsync(c => c.DeletedAt == null, cancellationToken),
            TotalPlays = tracks?.Plays ?? 0,
            TotalLikes = tracks?.Likes ?? 0,
            PendingReports = await db.Reports.CountAsync(r => r.Status == ReportStatus.Pending, cancellationToken),
            StorageBytesUsed = await db.TrackFiles.SumAsync(f => (long?)f.FileSize, cancellationToken) ?? 0,
            PlaysLast30Days = series,
        };
    }

    /// <summary>Crée un genre.</summary>
    public async Task<GenreDto> CreateGenreAsync(SaveGenreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAdmin();

        var name = RequireGenreName(request.Name);
        var slug = Tag.Normalize(name);

        if (await db.Genres.AnyAsync(g => g.Slug == slug, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.GenreAlreadyExists, "A genre with this name already exists.");
        }

        var genre = new Genre { Name = name, Slug = slug, CreatedAt = clock.UtcNow };
        db.Genres.Add(genre);

        await audit.RecordAsync("GENRE_CREATED", nameof(Genre), genre.Id, new { name }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new GenreDto(genre.Id, genre.Name, genre.Slug, 0);
    }

    /// <summary>Renomme un genre.</summary>
    public async Task<GenreDto> UpdateGenreAsync(Guid genreId, SaveGenreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAdmin();

        var genre = await db.Genres.FirstOrDefaultAsync(g => g.Id == genreId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.GenreNotFound, "The requested genre does not exist.");

        var name = RequireGenreName(request.Name);
        var slug = Tag.Normalize(name);

        if (await db.Genres.AnyAsync(g => g.Slug == slug && g.Id != genreId, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.GenreAlreadyExists, "A genre with this name already exists.");
        }

        genre.Name = name;
        genre.Slug = slug;

        await audit.RecordAsync("GENRE_UPDATED", nameof(Genre), genreId, new { name }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var trackCount = await db.Tracks.CountAsync(t => t.GenreId == genreId && t.DeletedAt == null, cancellationToken);
        return new GenreDto(genre.Id, genre.Name, genre.Slug, trackCount);
    }

    /// <summary>Supprime un genre qui n'est plus référencé par aucun morceau.</summary>
    public async Task DeleteGenreAsync(Guid genreId, CancellationToken cancellationToken)
    {
        EnsureAdmin();

        var genre = await db.Genres.FirstOrDefaultAsync(g => g.Id == genreId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.GenreNotFound, "The requested genre does not exist.");

        if (await db.Tracks.AnyAsync(t => t.GenreId == genreId, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.GenreInUse, "This genre is still used by at least one track.");
        }

        db.Genres.Remove(genre);
        await audit.RecordAsync("GENRE_DELETED", nameof(Genre), genreId, new { genre.Name }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Applique ou lève le masquage d'un morceau et journalise l'action.</summary>
    private async Task SetTrackHiddenAsync(Guid trackId, DateTime? hiddenAt, string action, CancellationToken cancellationToken)
    {
        var exists = await db.Tracks.AnyAsync(t => t.Id == trackId && t.DeletedAt == null, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");
        }

        await db.Tracks
            .Where(t => t.Id == trackId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.HiddenAt, hiddenAt), cancellationToken);

        await audit.RecordAsync(action, nameof(Track), trackId, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Projection SQL d'un utilisateur vers sa vue administrateur.</summary>
    private static AdminUserDto ProjectUser(User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Email = u.Email,
        Role = u.Role,
        Status = u.Status,
        ProfileVisibility = u.ProfileVisibility,
        TrackCount = u.Tracks.Count(t => t.DeletedAt == null),
        PlaylistCount = u.Playlists.Count,
        FollowerCount = u.Followers.Count,
        CreatedAt = u.CreatedAt,
        DeletedAt = u.DeletedAt,
    };

    /// <summary>Valide le nom d'un genre.</summary>
    private static string RequireGenreName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > 60)
        {
            throw new InputValidationException("name", "A genre name must be between 2 and 60 characters.");
        }

        return trimmed;
    }

    /// <summary>Refuse l'accès aux appelants sans privilège d'administration.</summary>
    private void EnsureAdmin()
    {
        if (!currentUser.Role.IsAdmin())
        {
            throw new ForbiddenException("Administrator privileges are required.");
        }
    }

    /// <summary>Refuse l'accès aux appelants sans privilège de modération.</summary>
    private void EnsureCanModerate()
    {
        if (!currentUser.Role.CanModerate())
        {
            throw new ForbiddenException("Moderation privileges are required.");
        }
    }
}
