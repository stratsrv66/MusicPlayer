using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>Flux binaire prêt à être renvoyé au client, avec ses en-têtes de cache.</summary>
/// <param name="Content">Flux positionnable, indispensable au support des requêtes Range.</param>
/// <param name="Length">Taille totale du contenu.</param>
/// <param name="ContentType">Type MIME à annoncer.</param>
/// <param name="ETag">Empreinte stable du contenu.</param>
/// <param name="LastModified">Date de dernière modification.</param>
public sealed record MediaStream(Stream Content, long Length, string ContentType, string ETag, DateTime LastModified);

/// <summary>
/// Résout le fichier audio d'un morceau pour le streaming HTTP.
/// Le flux est ouvert en lecture seule et jamais chargé en mémoire : le support des
/// requêtes <c>Range</c> est assuré par la couche API à partir de ce flux positionnable.
/// </summary>
public sealed class TrackStreamService(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    /// <summary>Ouvre le flux audio d'un morceau accessible à l'appelant.</summary>
    public async Task<MediaStream> OpenAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks
            .AsNoTracking()
            .Where(t => t.Id == trackId && t.DeletedAt == null)
            .Select(t => new
            {
                t.Id,
                t.OwnerId,
                t.Status,
                t.Visibility,
                t.HiddenAt,
                FilePath = t.File != null ? t.File.StoragePath : null,
                FileMime = t.File != null ? t.File.MimeType : null,
                FileChecksum = t.File != null ? t.File.Checksum : null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        var isOwner = currentUser.UserId is not null && currentUser.UserId == track.OwnerId;
        var isPrivileged = isOwner || currentUser.Role is UserRole.Moderator or UserRole.Admin;

        if (!isPrivileged)
        {
            var isPlayable = track.Status == TrackStatus.Ready && track.HiddenAt is null;
            if (!isPlayable || track.Visibility == ContentVisibility.Private)
            {
                throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");
            }
        }

        if (track.FilePath is null)
        {
            throw new UnprocessableException(ErrorCodes.TrackNotReady, "The audio file for this track is not available yet.");
        }

        var stat = await storage.StatAsync(track.FilePath, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackFileMissing, "The audio file is missing from storage.");

        var content = await storage.OpenReadAsync(track.FilePath, cancellationToken);
        return new MediaStream(
            content,
            stat.SizeBytes,
            track.FileMime ?? "application/octet-stream",
            $"\"{track.FileChecksum}\"",
            stat.LastModifiedUtc);
    }
}

/// <summary>Génération, remplacement et lecture des pochettes de morceaux.</summary>
public sealed class TrackCoverService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    IClock clock)
{
    /// <summary>Ouvre la pochette d'un morceau dans la taille demandée.</summary>
    public async Task<MediaStream> OpenAsync(Guid trackId, string sizeSlug, CancellationToken cancellationToken)
    {
        var size = ParseSize(sizeSlug);

        var cover = await db.TrackCovers
            .AsNoTracking()
            .Where(c => c.TrackId == trackId && c.Size == size)
            .Select(c => new { c.StoragePath, c.MimeType, c.Track.Visibility, c.Track.OwnerId, c.Track.DeletedAt, c.Track.HiddenAt })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.CoverNotFound, "This track has no cover art.");

        var isPrivileged = currentUser.UserId == cover.OwnerId || currentUser.Role is UserRole.Moderator or UserRole.Admin;
        var isVisible = cover.DeletedAt is null && cover.HiddenAt is null && cover.Visibility != ContentVisibility.Private;

        if (!isPrivileged && !isVisible)
        {
            throw new NotFoundException(ErrorCodes.CoverNotFound, "This track has no cover art.");
        }

        var stat = await storage.StatAsync(cover.StoragePath, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.CoverNotFound, "The cover file is missing from storage.");

        var content = await storage.OpenReadAsync(cover.StoragePath, cancellationToken);
        return new MediaStream(content, stat.SizeBytes, cover.MimeType, $"\"{trackId:N}-{sizeSlug}-{stat.LastModifiedUtc.Ticks}\"", stat.LastModifiedUtc);
    }

    /// <summary>Remplace la pochette d'un morceau par une image fournie par le propriétaire.</summary>
    public async Task ReplaceAsync(Guid trackId, UploadedImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        var track = await LoadForManagementAsync(trackId, cancellationToken);
        await GenerateAsync(track, image.Bytes, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Supprime la pochette d'un morceau.</summary>
    public async Task RemoveAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await LoadForManagementAsync(trackId, cancellationToken);
        await ClearAsync(track, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Génère et persiste les déclinaisons de pochette d'un morceau. Les anciennes
    /// déclinaisons sont supprimées au préalable, les chemins étant déterministes.
    /// Ne valide pas les droits : réservé au propriétaire ou au pipeline de traitement.
    /// </summary>
    public async Task GenerateAsync(Track track, byte[] source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(source);

        await ClearAsync(track, cancellationToken);
        var variants = imageProcessor.CreateCoverVariants(source);
        var now = clock.UtcNow;

        foreach (var variant in variants)
        {
            var path = StoragePaths.Cover(TrackMapper.CoverSlug(variant.Size), track.Id);
            using var buffer = new MemoryStream(variant.Bytes, writable: false);
            var written = await storage.SaveAsync(path, buffer, cancellationToken);

            db.TrackCovers.Add(new TrackCover
            {
                TrackId = track.Id,
                Size = variant.Size,
                StoragePath = path,
                MimeType = "image/webp",
                Width = variant.Width,
                Height = variant.Height,
                FileSize = written.SizeBytes,
                CreatedAt = now,
            });
        }
    }

    /// <summary>Supprime les enregistrements et les fichiers de pochette d'un morceau.</summary>
    public async Task ClearAsync(Track track, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);

        var covers = await db.TrackCovers.Where(c => c.TrackId == track.Id).ToListAsync(cancellationToken);
        foreach (var cover in covers)
        {
            db.TrackCovers.Remove(cover);
            await storage.DeleteAsync(cover.StoragePath, cancellationToken);
        }
    }

    /// <summary>Convertit un slug d'URL en taille de pochette.</summary>
    private static CoverSize ParseSize(string sizeSlug) => sizeSlug?.ToLowerInvariant() switch
    {
        "small" => CoverSize.Small,
        "medium" => CoverSize.Medium,
        "large" => CoverSize.Large,
        _ => throw new InputValidationException("size", "Cover size must be one of: small, medium, large."),
    };

    /// <summary>Charge un morceau modifiable par l'appelant.</summary>
    private async Task<Track> LoadForManagementAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == trackId && t.DeletedAt == null, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        if (!track.IsManageableBy(currentUser.UserId, currentUser.Role))
        {
            throw new ForbiddenException("You are not allowed to modify this track.", ErrorCodes.TrackAccessDenied);
        }

        return track;
    }
}

/// <summary>Image envoyée par le client, déjà chargée et validée en taille.</summary>
/// <param name="FileName">Nom d'origine.</param>
/// <param name="Bytes">Contenu binaire.</param>
public sealed record UploadedImage(string FileName, byte[] Bytes);
