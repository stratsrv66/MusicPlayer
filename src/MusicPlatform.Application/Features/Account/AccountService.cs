using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Account;

/// <summary>
/// Cas d'utilisation GenerateUserExport et DeleteUser.
///
/// La suppression est une anonymisation : la ligne utilisateur est conservée afin de ne pas
/// briser l'intégrité des événements d'écoute et du journal d'audit, mais toutes les données
/// personnelles, les contenus et les fichiers sont réellement supprimés.
/// </summary>
public sealed class AccountService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IBackgroundJobQueue jobs,
    IClock clock,
    ILogger<AccountService> logger)
{
    /// <summary>Durée de validité d'une archive d'export avant expiration.</summary>
    public static readonly TimeSpan ExportLifetime = TimeSpan.FromDays(7);

    /// <summary>Crée une demande d'export. Une seule demande peut être en cours à la fois.</summary>
    public async Task<UserExportDto> RequestExportAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var pending = await db.UserExports.AnyAsync(
            e => e.UserId == userId && (e.Status == UserExportStatus.Pending || e.Status == UserExportStatus.Processing),
            cancellationToken);

        if (pending)
        {
            throw new ConflictException(ErrorCodes.ExportAlreadyRunning, "An export is already being generated for this account.");
        }

        var export = new UserExport
        {
            UserId = userId,
            Status = UserExportStatus.Pending,
            CreatedAt = clock.UtcNow,
        };

        db.UserExports.Add(export);
        await db.SaveChangesAsync(cancellationToken);

        await jobs.EnqueueUserExportAsync(export.Id, cancellationToken);
        return ToDto(export);
    }

    /// <summary>Liste les exports de l'utilisateur connecté.</summary>
    public async Task<PagedResult<UserExportDto>> ListExportsAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var query = db.UserExports
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt);

        var result = await query.ToPagedResultAsync(page, cancellationToken);
        return result.Map(export => ToDto(export, now));
    }

    /// <summary>État d'un export appartenant à l'utilisateur connecté.</summary>
    public async Task<UserExportDto> GetExportAsync(Guid exportId, CancellationToken cancellationToken) =>
        ToDto(await LoadOwnExportAsync(exportId, cancellationToken), clock.UtcNow);

    /// <summary>Ouvre l'archive d'un export prêt et non expiré.</summary>
    public async Task<MediaStream> DownloadExportAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var export = await LoadOwnExportAsync(exportId, cancellationToken);

        if (!export.IsDownloadable(clock.UtcNow))
        {
            throw new UnprocessableException(ErrorCodes.ExportNotReady, "This export is not available for download.");
        }

        var path = export.StoragePath!;
        var stat = await storage.StatAsync(path, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.ExportNotFound, "The export archive is missing from storage.");

        var content = await storage.OpenReadAsync(path, cancellationToken);
        return new MediaStream(content, stat.SizeBytes, "application/zip", $"\"{exportId:N}\"", stat.LastModifiedUtc);
    }

    /// <summary>
    /// Supprime le compte de l'utilisateur connecté après confirmation explicite :
    /// la case de confirmation et la saisie du pseudo sont toutes deux obligatoires.
    /// </summary>
    public async Task DeleteOwnAccountAsync(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new { u.Username })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The current user no longer exists.");

        var confirmationMatches = string.Equals(request.ConfirmUsername?.Trim(), user.Username, StringComparison.Ordinal);
        if (!request.Confirm || !confirmationMatches)
        {
            throw new UnprocessableException(
                ErrorCodes.AccountDeletionNotConfirmed,
                "Account deletion requires an explicit confirmation and the exact username.");
        }

        await PurgeUserAsync(userId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Supprime tous les contenus, fichiers et données personnelles d'un utilisateur et
    /// anonymise son compte. Ne persiste pas : l'appelant contrôle la transaction.
    /// </summary>
    public async Task PurgeUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");

        var paths = await CollectUserFilesAsync(userId, cancellationToken);

        // Les événements d'écoute sont conservés mais dissociés de la personne.
        await db.PlayEvents
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UserId, (Guid?)null), cancellationToken);

        await db.AuditLogs
            .Where(l => l.ActorId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ActorId, (Guid?)null), cancellationToken);

        await db.Reports
            .Where(r => r.ReviewedBy == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewedBy, (Guid?)null), cancellationToken);

        // Les suppressions en cascade configurées côté base retirent les entités dépendantes
        // (fichiers de morceaux, pochettes, likes, items de playlist, commentaires...).
        await db.Tracks.Where(t => t.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Playlists.Where(p => p.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Comments.Where(c => c.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.TrackLikes.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Follows.Where(f => f.FollowerId == userId || f.FollowedId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.PlaylistFollows.Where(f => f.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.PlaylistFavorites.Where(f => f.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ListeningHistories.Where(h => h.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserExports.Where(e => e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Reports.Where(r => r.ReporterId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UploadOperations.Where(o => o.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        Anonymize(user, clock.UtcNow);

        await DeleteFilesAsync(paths, cancellationToken);
        await storage.DeleteDirectoryAsync(StoragePaths.AudioDirectory(userId), cancellationToken);
        await storage.DeleteDirectoryAsync(StoragePaths.ExportDirectory(userId), cancellationToken);

        logger.LogInformation("Account {UserId} purged.", userId);
    }

    /// <summary>Remplace les données personnelles par des valeurs neutres et irréversibles.</summary>
    private static void Anonymize(User user, DateTime now)
    {
        var suffix = user.Id.ToString("N")[..8];

        user.Email = $"deleted-{suffix}@deleted.invalid";
        user.Username = $"deleted-{suffix}";
        user.UsernameNormalized = user.Username;
        user.PasswordHash = string.Empty;
        user.Bio = null;
        user.SocialLinks = null;
        user.AvatarFileId = null;
        user.ProfileVisibility = ProfileVisibility.Private;
        user.Status = UserStatus.Suspended;
        user.DeletedAt = now;
        user.UpdatedAt = now;
    }

    /// <summary>Rassemble les chemins de tous les fichiers appartenant à l'utilisateur.</summary>
    private async Task<List<string>> CollectUserFilesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var paths = new List<string>();

        paths.AddRange(await db.TrackFiles
            .Where(f => f.Track.OwnerId == userId)
            .Select(f => f.StoragePath)
            .ToListAsync(cancellationToken));

        paths.AddRange(await db.TrackCovers
            .Where(c => c.Track.OwnerId == userId)
            .Select(c => c.StoragePath)
            .ToListAsync(cancellationToken));

        paths.AddRange(await db.Playlists
            .Where(p => p.OwnerId == userId && p.CoverFile != null)
            .Select(p => p.CoverFile!.StoragePath)
            .ToListAsync(cancellationToken));

        paths.AddRange(await db.Users
            .Where(u => u.Id == userId && u.AvatarFile != null)
            .Select(u => u.AvatarFile!.StoragePath)
            .ToListAsync(cancellationToken));

        paths.AddRange(await db.UserExports
            .Where(e => e.UserId == userId && e.StoragePath != null)
            .Select(e => e.StoragePath!)
            .ToListAsync(cancellationToken));

        return paths;
    }

    /// <summary>Supprime une liste de fichiers en journalisant les échecs sans les propager.</summary>
    private async Task DeleteFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            try
            {
                await storage.DeleteAsync(paths[i], cancellationToken);
            }
            catch (IOException exception)
            {
                logger.LogError(exception, "Could not delete file {Path} during account purge.", paths[i]);
            }
        }
    }

    /// <summary>Charge un export appartenant à l'utilisateur connecté.</summary>
    private async Task<UserExport> LoadOwnExportAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        return await db.UserExports
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == exportId && e.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.ExportNotFound, "The requested export does not exist.");
    }

    /// <summary>Convertit un export en DTO, en marquant comme expirés ceux dont la date est passée.</summary>
    private static UserExportDto ToDto(UserExport export, DateTime? now = null)
    {
        var reference = now ?? export.CreatedAt;
        var isExpired = export.Status == UserExportStatus.Ready && export.ExpiresAt is { } expiry && expiry <= reference;
        var status = isExpired ? UserExportStatus.Expired : export.Status;

        return new UserExportDto
        {
            Id = export.Id,
            Status = status,
            FileSize = export.FileSize,
            FailureReason = export.FailureReason,
            ExpiresAt = export.ExpiresAt,
            CreatedAt = export.CreatedAt,
            CompletedAt = export.CompletedAt,
            DownloadUrl = status == UserExportStatus.Ready
                ? $"{MediaUrls.Base}/me/data-exports/{export.Id}/download"
                : null,
        };
    }
}
