using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>
/// Cas d'utilisation portant sur les morceaux : listage, consultation, création avec upload,
/// remplacement du fichier, mise à jour des métadonnées, publication et suppression.
/// </summary>
public sealed class TrackService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IBackgroundJobQueue jobs,
    TagResolver tagResolver,
    IClock clock,
    ILogger<TrackService> logger)
{
    /// <summary>Liste paginée des morceaux visibles par l'appelant, filtrée et triée.</summary>
    public async Task<PagedResult<TrackDto>> ListAsync(TrackFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Tracks.AsNoTracking().PubliclyListed();
        query = ApplyFilter(query, filter);
        query = query.ApplySort(filter.Sort);

        return await query.ToTrackPageAsync(page, currentUser.UserId, currentUser.Role, cancellationToken);
    }

    /// <summary>Détail d'un morceau, sous réserve que l'appelant y ait accès.</summary>
    public async Task<TrackDetailsDto> GetAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var projection = await db.Tracks
            .AsNoTracking()
            .Where(t => t.Id == trackId && t.DeletedAt == null)
            .Project(currentUser.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        EnsureReadable(projection);

        var commentCount = await db.Comments
            .CountAsync(c => c.TrackId == trackId && c.DeletedAt == null, cancellationToken);

        return projection.ToDetailsDto(currentUser.UserId, currentUser.Role, commentCount);
    }

    /// <summary>Morceaux publiés par un utilisateur donné.</summary>
    public async Task<PagedResult<TrackDto>> ListByUserAsync(string username, PageRequest page, CancellationToken cancellationToken)
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
            return PagedResult<TrackDto>.Empty(page.Page, page.PageSize);
        }

        var query = isPrivileged
            ? db.Tracks.AsNoTracking().Where(t => t.OwnerId == owner.Id && t.DeletedAt == null)
            : db.Tracks.AsNoTracking().PubliclyListed().Where(t => t.OwnerId == owner.Id);

        return await query.ApplySort("recent").ToTrackPageAsync(page, currentUser.UserId, currentUser.Role, cancellationToken);
    }

    /// <summary>Morceaux publics portant un tag donné.</summary>
    public async Task<PagedResult<TrackDto>> ListByTagAsync(string tag, PageRequest page, string? sort, CancellationToken cancellationToken)
    {
        var slug = Tag.Normalize(tag);
        var query = db.Tracks.AsNoTracking().PubliclyListed()
            .Where(t => t.TrackTags.Any(tt => tt.Tag.Slug == slug));

        return await query.ApplySort(sort).ToTrackPageAsync(page, currentUser.UserId, currentUser.Role, cancellationToken);
    }

    /// <summary>Morceaux publics d'un genre donné.</summary>
    public async Task<PagedResult<TrackDto>> ListByGenreAsync(Guid genreId, PageRequest page, string? sort, CancellationToken cancellationToken)
    {
        if (!await db.Genres.AnyAsync(g => g.Id == genreId, cancellationToken))
        {
            throw new NotFoundException(ErrorCodes.GenreNotFound, "The requested genre does not exist.");
        }

        var query = db.Tracks.AsNoTracking().PubliclyListed().Where(t => t.GenreId == genreId);
        return await query.ApplySort(sort).ToTrackPageAsync(page, currentUser.UserId, currentUser.Role, cancellationToken);
    }

    /// <summary>
    /// Crée un morceau et enregistre son fichier audio. Le fichier est écrit en zone temporaire
    /// puis traité en arrière-plan : la requête HTTP ne porte pas le coût de l'analyse.
    /// </summary>
    public Task<UploadAcceptedDto> CreateAsync(CreateTrackRequest request, UploadedFile file, CancellationToken cancellationToken) =>
        CreateForOwnerAsync(currentUser.RequireUserId(), request, file, cancellationToken);

    /// <summary>
    /// Crée un morceau au nom d'un propriétaire explicite.
    ///
    /// Cette variante existe pour les traitements exécutés hors requête HTTP — l'import
    /// d'une playlist s'exécute en arrière-plan et ne dispose donc d'aucun utilisateur
    /// courant. Le contrôle d'accès incombe à l'appelant, qui doit avoir vérifié que
    /// <paramref name="ownerId"/> est bien à l'origine de l'opération.
    /// </summary>
    public async Task<UploadAcceptedDto> CreateForOwnerAsync(
        Guid ownerId,
        CreateTrackRequest request,
        UploadedFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(file);

        var userId = ownerId;
        var extension = AudioFileValidator.ValidateNameAndSize(file.FileName, file.Length);
        await ValidateReferencesAsync(request.AlbumId, request.GenreId, cancellationToken);

        var now = clock.UtcNow;
        var owner = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Username)
            .FirstAsync(cancellationToken);

        var track = new Track
        {
            OwnerId = userId,
            Title = FallbackTitle(request.Title, file.FileName),
            ArtistName = string.IsNullOrWhiteSpace(request.ArtistName) ? owner : request.ArtistName.Trim(),
            Description = request.Description?.Trim(),
            AlbumId = request.AlbumId,
            GenreId = request.GenreId,
            Year = request.Year,
            Visibility = request.Visibility,
            Status = TrackStatus.Uploading,
            DurationSeconds = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Tracks.Add(track);
        await tagResolver.ApplyAsync(track, request.Tags, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return await StoreUploadAsync(track, file, extension, cancellationToken);
    }

    /// <summary>Remplace le fichier audio d'un morceau existant.</summary>
    public async Task<UploadAcceptedDto> ReplaceFileAsync(Guid trackId, UploadedFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        var track = await LoadForManagementAsync(trackId, cancellationToken);
        var extension = AudioFileValidator.ValidateNameAndSize(file.FileName, file.Length);

        var hasPendingUpload = await db.UploadOperations.AnyAsync(
            o => o.TrackId == trackId
                 && (o.Status == UploadOperationStatus.Uploading || o.Status == UploadOperationStatus.Processing),
            cancellationToken);

        if (hasPendingUpload)
        {
            throw new ConflictException(ErrorCodes.Conflict, "Another upload is already in progress for this track.");
        }

        track.Status = TrackStatus.Uploading;
        track.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await StoreUploadAsync(track, file, extension, cancellationToken);
    }

    /// <summary>Met à jour les métadonnées d'un morceau appartenant à l'appelant.</summary>
    public async Task<TrackDetailsDto> UpdateAsync(Guid trackId, UpdateTrackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var track = await LoadForManagementAsync(trackId, cancellationToken);
        await ValidateReferencesAsync(request.AlbumId, request.GenreId, cancellationToken);

        if (request.Title is not null)
        {
            track.Title = request.Title.Trim();
        }

        if (request.ArtistName is not null)
        {
            track.ArtistName = request.ArtistName.Trim();
        }

        if (request.Description is not null)
        {
            track.Description = request.Description.Trim();
        }

        if (request.Year is not null)
        {
            track.Year = request.Year;
        }

        track.AlbumId = request.ClearAlbum ? null : request.AlbumId ?? track.AlbumId;
        track.GenreId = request.ClearGenre ? null : request.GenreId ?? track.GenreId;

        ApplyVisibility(track, request.Visibility);

        if (request.Tags is not null)
        {
            await tagResolver.ApplyAsync(track, request.Tags, cancellationToken);
        }

        track.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(trackId, cancellationToken);
    }

    /// <summary>Publie un morceau une fois son traitement terminé.</summary>
    public async Task<TrackDetailsDto> PublishAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await LoadForManagementAsync(trackId, cancellationToken);
        track.Publish(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(trackId, cancellationToken);
    }

    /// <summary>Retire un morceau de la publication.</summary>
    public async Task<TrackDetailsDto> UnpublishAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await LoadForManagementAsync(trackId, cancellationToken);
        track.Unpublish(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(trackId, cancellationToken);
    }

    /// <summary>
    /// Supprime un morceau et ses fichiers. La base est mise à jour d'abord ; les fichiers
    /// sont ensuite effacés, un échec de suppression physique n'invalidant pas l'opération.
    /// </summary>
    public async Task DeleteAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks
            .Include(t => t.File)
            .Include(t => t.Covers)
            .FirstOrDefaultAsync(t => t.Id == trackId && t.DeletedAt == null, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        if (!track.IsManageableBy(currentUser.UserId, currentUser.Role))
        {
            throw new ForbiddenException("You are not allowed to delete this track.", ErrorCodes.TrackAccessDenied);
        }

        var paths = CollectStoragePaths(track);
        db.Tracks.Remove(track);
        await db.SaveChangesAsync(cancellationToken);

        await DeleteFilesAsync(paths, cancellationToken);
        logger.LogInformation("Track {TrackId} deleted by user {UserId}.", trackId, currentUser.UserId);
    }

    /// <summary>
    /// Charge un morceau modifiable par l'appelant ou lève l'erreur appropriée.
    ///
    /// Un morceau que l'appelant ne peut pas voir renvoie 404 et non 403 : répondre
    /// « interdit » révélerait l'existence d'un contenu privé.
    /// </summary>
    private async Task<Track> LoadForManagementAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks
            .Include(t => t.TrackTags)
            .FirstOrDefaultAsync(t => t.Id == trackId && t.DeletedAt == null, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        if (!track.IsAccessibleBy(currentUser.UserId, currentUser.Role))
        {
            throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");
        }

        if (!track.IsManageableBy(currentUser.UserId, currentUser.Role))
        {
            throw new ForbiddenException("You are not allowed to modify this track.", ErrorCodes.TrackAccessDenied);
        }

        return track;
    }

    /// <summary>
    /// Écrit le fichier reçu en zone temporaire, en vérifie la signature binaire, puis
    /// programme le traitement. En cas de rejet, le fichier temporaire est supprimé.
    /// </summary>
    private async Task<UploadAcceptedDto> StoreUploadAsync(Track track, UploadedFile file, string extension, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var operation = new UploadOperation
        {
            UserId = track.OwnerId,
            TrackId = track.Id,
            Status = UploadOperationStatus.Uploading,
            OriginalFilename = Path.GetFileName(file.FileName),
            MimeType = AudioFileValidator.ResolveContentType(extension) ?? file.ContentType,
            FileSize = file.Length,
            CreatedAt = now,
            UpdatedAt = now,
        };
        operation.TemporaryPath = StoragePaths.Temp(operation.Id, extension);

        db.UploadOperations.Add(operation);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await using var source = file.OpenReadStream();
            var written = await storage.SaveAsync(operation.TemporaryPath, source, cancellationToken);
            operation.FileSize = written.SizeBytes;
            await EnsureSignatureAsync(operation.TemporaryPath, cancellationToken);
        }
        catch (Exception exception)
        {
            await AbortUploadAsync(operation, track, exception.Message, cancellationToken);
            throw;
        }

        operation.Status = UploadOperationStatus.Processing;
        operation.UpdatedAt = clock.UtcNow;
        track.Status = TrackStatus.Processing;
        track.UpdatedAt = operation.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);

        await jobs.EnqueueTrackProcessingAsync(operation.Id, cancellationToken);
        return new UploadAcceptedDto(track.Id, operation.Id, track.Status);
    }

    /// <summary>Lit l'en-tête du fichier stocké et rejette un contenu non audio.</summary>
    private async Task EnsureSignatureAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(relativePath, cancellationToken);
        var header = new byte[AudioFileValidator.SignatureLength];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        AudioFileValidator.EnsureAudioSignature(header.AsSpan(0, read));
    }

    /// <summary>Nettoie une opération d'upload interrompue : fichier temporaire et états en base.</summary>
    private async Task AbortUploadAsync(UploadOperation operation, Track track, string reason, CancellationToken cancellationToken)
    {
        if (operation.TemporaryPath is not null)
        {
            await storage.DeleteAsync(operation.TemporaryPath, CancellationToken.None);
        }

        operation.Status = UploadOperationStatus.Failed;
        operation.FailureReason = reason;
        operation.TemporaryPath = null;
        operation.CompletedAt = clock.UtcNow;
        operation.UpdatedAt = operation.CompletedAt.Value;
        track.MarkFailed(reason, operation.UpdatedAt);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Upload {OperationId} for track {TrackId} aborted: {Reason}", operation.Id, track.Id, reason);
    }

    /// <summary>Vérifie que la visibilité demandée est applicable à l'état du morceau.</summary>
    private void ApplyVisibility(Track track, ContentVisibility? visibility)
    {
        if (visibility is null || visibility == track.Visibility)
        {
            return;
        }

        if (visibility != ContentVisibility.Private && track.Status != TrackStatus.Ready)
        {
            throw new UnprocessableException(ErrorCodes.TrackNotReady, "The track cannot be shared before processing completes.");
        }

        track.Visibility = visibility.Value;
        if (visibility == ContentVisibility.Public)
        {
            track.PublishedAt ??= clock.UtcNow;
        }
    }

    /// <summary>Refuse l'accès à un morceau non visible par l'appelant.</summary>
    private void EnsureReadable(TrackProjection projection)
    {
        var isOwner = currentUser.UserId is not null && currentUser.UserId == projection.OwnerId;
        var isPrivileged = isOwner || currentUser.Role is UserRole.Moderator or UserRole.Admin;

        if (isPrivileged)
        {
            return;
        }

        var isPlayable = projection.Status == TrackStatus.Ready && projection.HiddenAt is null;
        if (!isPlayable || projection.Visibility == ContentVisibility.Private)
        {
            // On renvoie 404 plutôt que 403 pour ne pas révéler l'existence d'un contenu privé.
            throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");
        }
    }

    /// <summary>Vérifie que l'album et le genre référencés existent réellement.</summary>
    private async Task ValidateReferencesAsync(Guid? albumId, Guid? genreId, CancellationToken cancellationToken)
    {
        if (albumId is not null && !await db.Albums.AnyAsync(a => a.Id == albumId, cancellationToken))
        {
            throw new NotFoundException(ErrorCodes.AlbumNotFound, "The referenced album does not exist.");
        }

        if (genreId is not null && !await db.Genres.AnyAsync(g => g.Id == genreId, cancellationToken))
        {
            throw new NotFoundException(ErrorCodes.GenreNotFound, "The referenced genre does not exist.");
        }
    }

    /// <summary>Rassemble tous les chemins de stockage à supprimer avec le morceau.</summary>
    private static List<string> CollectStoragePaths(Track track)
    {
        var paths = new List<string>(track.Covers.Count + 1);
        if (track.File is not null)
        {
            paths.Add(track.File.StoragePath);
        }

        foreach (var cover in track.Covers)
        {
            paths.Add(cover.StoragePath);
        }

        return paths;
    }

    /// <summary>
    /// Supprime des fichiers déjà déréférencés en base. Un échec est journalisé sans être
    /// propagé : le fichier devient orphelin et sera repris par le nettoyage périodique.
    /// </summary>
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
                logger.LogError(exception, "Could not delete orphaned file {Path}.", paths[i]);
            }
        }
    }

    /// <summary>Retient le titre fourni, ou à défaut le nom du fichier sans extension.</summary>
    private static string FallbackTitle(string? title, string fileName) =>
        string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim();

    /// <summary>Applique les filtres de listage sur une requête de morceaux.</summary>
    internal static IQueryable<Track> ApplyFilter(IQueryable<Track> query, TrackFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query.Trim();
            if (term.StartsWith('#'))
            {
                var slug = Tag.Normalize(term);
                query = query.Where(t => t.TrackTags.Any(tt => tt.Tag.Slug == slug));
            }
            else
            {
                var pattern = SqlPatterns.Contains(term);
                query = query.Where(t => EF.Functions.Like(t.Title.ToLower(), pattern, SqlPatterns.EscapeCharacter)
                                         || EF.Functions.Like(t.ArtistName.ToLower(), pattern, SqlPatterns.EscapeCharacter)
                                         || EF.Functions.Like(t.Owner.Username.ToLower(), pattern, SqlPatterns.EscapeCharacter));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Genre))
        {
            var genre = filter.Genre.Trim().ToLowerInvariant();
            query = query.Where(t => t.Genre != null && t.Genre.Slug == genre);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var tag = Tag.Normalize(filter.Tag);
            query = query.Where(t => t.TrackTags.Any(tt => tt.Tag.Slug == tag));
        }

        if (!string.IsNullOrWhiteSpace(filter.Artist))
        {
            var artist = SqlPatterns.Contains(filter.Artist);
            query = query.Where(t => EF.Functions.Like(t.ArtistName.ToLower(), artist, SqlPatterns.EscapeCharacter)
                                     || EF.Functions.Like(t.Owner.Username.ToLower(), artist, SqlPatterns.EscapeCharacter));
        }

        if (filter.MinDuration is { } min)
        {
            query = query.Where(t => t.DurationSeconds >= min);
        }

        if (filter.MaxDuration is { } max)
        {
            query = query.Where(t => t.DurationSeconds <= max);
        }

        if (filter.From is { } from)
        {
            query = query.Where(t => t.CreatedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(t => t.CreatedAt <= to);
        }

        return query;
    }
}
