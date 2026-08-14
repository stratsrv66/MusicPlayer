using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Account;

/// <summary>
/// Génère l'archive ZIP contenant les données personnelles d'un utilisateur.
/// L'archive est écrite directement dans le stockage via un flux : elle n'est jamais
/// assemblée intégralement en mémoire.
/// </summary>
public sealed class UserExportGenerator(
    IAppDbContext db,
    IFileStorage storage,
    IClock clock,
    ILogger<UserExportGenerator> logger)
{
    /// <summary>Nombre maximal de lignes exportées par section, pour borner l'archive.</summary>
    private const int MaxRowsPerSection = 20000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>Traite une demande d'export. Idempotent : une demande déjà traitée est ignorée.</summary>
    public async Task GenerateAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var export = await db.UserExports.FirstOrDefaultAsync(e => e.Id == exportId, cancellationToken);
        if (export is null || export.Status != UserExportStatus.Pending)
        {
            logger.LogDebug("Export {ExportId} is not pending.", exportId);
            return;
        }

        export.Status = UserExportStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

        var path = StoragePaths.Export(export.UserId, export.Id);

        try
        {
            await WriteArchiveAsync(export.UserId, path, cancellationToken);

            var stat = await storage.StatAsync(path, cancellationToken);
            var now = clock.UtcNow;

            export.Status = UserExportStatus.Ready;
            export.StoragePath = path;
            export.FileSize = stat?.SizeBytes;
            export.CompletedAt = now;
            export.ExpiresAt = now.Add(AccountService.ExportLifetime);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Export {ExportId} is ready ({Size} bytes).", exportId, export.FileSize);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Export {ExportId} failed.", exportId);
            await storage.DeleteAsync(path, CancellationToken.None);

            export.Status = UserExportStatus.Failed;
            export.FailureReason = "The export archive could not be generated.";
            export.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Écrit l'ensemble des sections de données dans l'archive.</summary>
    private async Task WriteArchiveAsync(Guid userId, string path, CancellationToken cancellationToken)
    {
        await using var output = await storage.OpenWriteAsync(path, cancellationToken);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await WriteEntryAsync(archive, "profile.json", await LoadProfileAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "settings.json", await LoadSettingsAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "tracks.json", await LoadTracksAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "playlists.json", await LoadPlaylistsAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "likes.json", await LoadLikesAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "comments.json", await LoadCommentsAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "following.json", await LoadFollowingAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "followers.json", await LoadFollowersAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "listening-history.json", await LoadHistoryAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "reports.json", await LoadReportsAsync(userId, cancellationToken), cancellationToken);
        await WriteEntryAsync(archive, "README.txt", ReadmeText(), cancellationToken);
    }

    /// <summary>Ajoute une entrée JSON à l'archive.</summary>
    private static async Task WriteEntryAsync<T>(ZipArchive archive, string name, T payload, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();

        if (payload is string text)
        {
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync(text.AsMemory(), cancellationToken);
            return;
        }

        await JsonSerializer.SerializeAsync(entryStream, payload, JsonOptions, cancellationToken);
    }

    private async Task<object?> LoadProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.Username,
                u.Bio,
                u.SocialLinks,
                ProfileVisibility = u.ProfileVisibility.ToString(),
                Role = u.Role.ToString(),
                u.CreatedAt,
                u.UpdatedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<object?> LoadSettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserSettings
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.ShowLikeCount, s.ShowPlayCount })
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<object> LoadTracksAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Tracks
            .AsNoTracking()
            .Where(t => t.OwnerId == userId && t.DeletedAt == null)
            .OrderBy(t => t.CreatedAt)
            .Take(MaxRowsPerSection)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.ArtistName,
                t.Description,
                t.Year,
                t.DurationSeconds,
                Visibility = t.Visibility.ToString(),
                Status = t.Status.ToString(),
                Genre = t.Genre != null ? t.Genre.Name : null,
                Album = t.Album != null ? t.Album.Name : null,
                Tags = t.TrackTags.Select(tt => tt.Tag.Name).ToList(),
                t.PlayCount,
                t.LikeCount,
                t.CreatedAt,
                t.PublishedAt,
            })
            .ToListAsync(cancellationToken);

    private async Task<object> LoadPlaylistsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Playlists
            .AsNoTracking()
            .Where(p => p.OwnerId == userId)
            .OrderBy(p => p.CreatedAt)
            .Take(MaxRowsPerSection)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                Visibility = p.Visibility.ToString(),
                p.CreatedAt,
                Tracks = p.Items.OrderBy(i => i.Position)
                    .Select(i => new { i.Position, i.TrackId, i.Track.Title, i.Track.ArtistName, i.AddedAt })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

    private async Task<object> LoadLikesAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.TrackLikes
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(MaxRowsPerSection)
            .Select(l => new { l.TrackId, l.Track.Title, l.Track.ArtistName, l.CreatedAt })
            .ToListAsync(cancellationToken);

    private async Task<object> LoadCommentsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Comments
            .AsNoTracking()
            .Where(c => c.AuthorId == userId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .Take(MaxRowsPerSection)
            .Select(c => new { c.Id, c.TrackId, c.Track.Title, c.Content, c.TimestampSeconds, c.CreatedAt, c.UpdatedAt })
            .ToListAsync(cancellationToken);

    private async Task<object> LoadFollowingAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Follows
            .AsNoTracking()
            .Where(f => f.FollowerId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(MaxRowsPerSection)
            .Select(f => new { f.FollowedId, f.Followed.Username, f.CreatedAt })
            .ToListAsync(cancellationToken);

    private async Task<object> LoadFollowersAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Follows
            .AsNoTracking()
            .Where(f => f.FollowedId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(MaxRowsPerSection)
            .Select(f => new { f.FollowerId, f.Follower.Username, f.CreatedAt })
            .ToListAsync(cancellationToken);

    private async Task<object> LoadHistoryAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ListeningHistories
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.LastPlayedAt)
            .Take(MaxRowsPerSection)
            .Select(h => new { h.TrackId, h.Track.Title, h.Track.ArtistName, h.LastPositionSeconds, h.LastPlayedAt })
            .ToListAsync(cancellationToken);

    private async Task<object> LoadReportsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Reports
            .AsNoTracking()
            .Where(r => r.ReporterId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(MaxRowsPerSection)
            .Select(r => new
            {
                r.Id,
                TargetType = r.TargetType.ToString(),
                r.TargetId,
                Reason = r.Reason.ToString(),
                r.Description,
                Status = r.Status.ToString(),
                r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

    /// <summary>Note explicative jointe à l'archive.</summary>
    private static string ReadmeText() =>
        """
        Export de vos données personnelles
        ==================================

        Cette archive contient les données rattachées à votre compte, au format JSON.

          profile.json           Informations de profil
          settings.json          Préférences d'affichage
          tracks.json            Métadonnées de vos morceaux
          playlists.json         Vos playlists et leur contenu
          likes.json             Morceaux que vous avez aimés
          comments.json          Vos commentaires
          following.json         Comptes que vous suivez
          followers.json         Comptes qui vous suivent
          listening-history.json Votre historique d'écoute
          reports.json           Signalements que vous avez émis

        Les fichiers audio ne sont pas inclus : ils restent téléchargeables
        individuellement depuis votre bibliothèque tant que votre compte est actif.

        Cette archive est supprimée automatiquement après 7 jours.
        """;
}
