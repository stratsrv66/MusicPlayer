using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>
/// Pipeline de traitement d'un fichier audio, exécuté hors de la requête HTTP :
/// extraction des métadonnées, extraction et redimensionnement de la pochette,
/// promotion du fichier temporaire vers son emplacement définitif.
/// </summary>
public sealed class TrackProcessingService(
    IAppDbContext db,
    IFileStorage storage,
    IAudioMetadataExtractor metadataExtractor,
    TrackCoverService coverService,
    IClock clock,
    ILogger<TrackProcessingService> logger)
{
    /// <summary>
    /// Traite une opération d'upload. La méthode est idempotente : une opération qui n'est
    /// plus en état <c>PROCESSING</c> est ignorée.
    /// </summary>
    public async Task ProcessAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await db.UploadOperations
            .Include(o => o.Track)
            .FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken);

        if (operation is null || operation.Status != UploadOperationStatus.Processing)
        {
            logger.LogDebug("Upload operation {OperationId} is not pending processing.", operationId);
            return;
        }

        var track = operation.Track;
        if (track is null || operation.TemporaryPath is null)
        {
            await FailAsync(operation, null, "The upload operation is missing its track or temporary file.", cancellationToken);
            return;
        }

        try
        {
            await RunPipelineAsync(operation, track, operation.TemporaryPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // L'arrêt de l'application ne doit pas marquer l'upload comme définitivement échoué.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Processing failed for upload {OperationId}.", operationId);
            await FailAsync(operation, track, "The audio file could not be processed.", cancellationToken);
        }
    }

    /// <summary>Enchaîne les étapes du pipeline pour un fichier temporaire validé.</summary>
    private async Task RunPipelineAsync(UploadOperation operation, Track track, string temporaryPath, CancellationToken cancellationToken)
    {
        var metadata = await metadataExtractor.ExtractAsync(temporaryPath, operation.OriginalFilename, cancellationToken);
        if (metadata is null || metadata.DurationSeconds <= 0)
        {
            await FailAsync(operation, track, "The file is not a readable audio track.", cancellationToken);
            return;
        }

        var previousPath = await db.TrackFiles
            .Where(f => f.TrackId == track.Id)
            .Select(f => f.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        var extension = Path.GetExtension(temporaryPath);
        var finalPath = StoragePaths.Audio(track.OwnerId, track.Id, extension);

        await using (var source = await storage.OpenReadAsync(temporaryPath, cancellationToken))
        {
            var written = await storage.SaveAsync(finalPath, source, cancellationToken);
            await UpsertTrackFileAsync(track, finalPath, operation.MimeType, written, cancellationToken);
        }

        await UpsertMetadataAsync(track, operation, metadata, cancellationToken);
        ApplyMetadataDefaults(track, metadata);

        if (metadata.EmbeddedCover is { Length: > 0 } && !await db.TrackCovers.AnyAsync(c => c.TrackId == track.Id, cancellationToken))
        {
            await GenerateCoverSafelyAsync(track, metadata.EmbeddedCover, cancellationToken);
        }

        var now = clock.UtcNow;
        track.MarkReady(metadata.DurationSeconds, now);
        operation.Status = UploadOperationStatus.Ready;
        operation.CompletedAt = now;
        operation.UpdatedAt = now;
        operation.TemporaryPath = null;

        await db.SaveChangesAsync(cancellationToken);

        // Les fichiers ne participent pas à la transaction : ils sont nettoyés une fois la base cohérente.
        await storage.DeleteAsync(temporaryPath, CancellationToken.None);
        if (previousPath is not null && previousPath != finalPath)
        {
            await storage.DeleteAsync(previousPath, CancellationToken.None);
        }

        logger.LogInformation("Track {TrackId} is ready ({Duration}s).", track.Id, metadata.DurationSeconds);
    }

    /// <summary>Crée ou met à jour la référence vers le fichier audio de diffusion.</summary>
    private async Task UpsertTrackFileAsync(Track track, string path, string mimeType, FileWriteResult written, CancellationToken cancellationToken)
    {
        var file = await db.TrackFiles.FirstOrDefaultAsync(f => f.TrackId == track.Id, cancellationToken);
        if (file is null)
        {
            file = new TrackFile { TrackId = track.Id };
            db.TrackFiles.Add(file);
        }

        file.StoragePath = path;
        file.MimeType = mimeType;
        file.FileSize = written.SizeBytes;
        file.Checksum = written.Sha256;
        file.CreatedAt = clock.UtcNow;
    }

    /// <summary>Crée ou met à jour les métadonnées extraites du fichier d'origine.</summary>
    private async Task UpsertMetadataAsync(Track track, UploadOperation operation, AudioMetadata metadata, CancellationToken cancellationToken)
    {
        var entity = await db.TrackMetadata.FirstOrDefaultAsync(m => m.TrackId == track.Id, cancellationToken);
        if (entity is null)
        {
            entity = new TrackMetadata { TrackId = track.Id };
            db.TrackMetadata.Add(entity);
        }

        entity.OriginalFilename = operation.OriginalFilename;
        entity.EmbeddedTitle = metadata.Title;
        entity.EmbeddedArtist = metadata.Artist;
        entity.EmbeddedAlbum = metadata.Album;
        entity.EmbeddedGenre = metadata.Genre;
        entity.EmbeddedYear = metadata.Year;
        entity.MetadataJson = JsonSerializer.Serialize(new
        {
            metadata.Bitrate,
            metadata.SampleRate,
            metadata.Channels,
            metadata.Codec,
            metadata.DurationSeconds,
        });
    }

    /// <summary>
    /// Complète les champs laissés vides par l'utilisateur avec les métadonnées du fichier.
    /// Les valeurs saisies explicitement ne sont jamais écrasées.
    /// </summary>
    private static void ApplyMetadataDefaults(Track track, AudioMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(track.Title) && !string.IsNullOrWhiteSpace(metadata.Title))
        {
            track.Title = metadata.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(metadata.Artist) && string.IsNullOrWhiteSpace(track.ArtistName))
        {
            track.ArtistName = metadata.Artist.Trim();
        }

        track.Year ??= metadata.Year;
    }

    /// <summary>
    /// Génère la pochette embarquée sans faire échouer le morceau : une image illisible
    /// n'empêche pas la mise à disposition de l'audio.
    /// </summary>
    private async Task GenerateCoverSafelyAsync(Track track, byte[] cover, CancellationToken cancellationToken)
    {
        try
        {
            await coverService.GenerateAsync(track, cover, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Embedded cover of track {TrackId} could not be processed.", track.Id);
        }
    }

    /// <summary>Marque l'opération et le morceau en échec, et supprime le fichier temporaire.</summary>
    private async Task FailAsync(UploadOperation operation, Track? track, string reason, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (operation.TemporaryPath is not null)
        {
            await storage.DeleteAsync(operation.TemporaryPath, CancellationToken.None);
            operation.TemporaryPath = null;
        }

        operation.Status = UploadOperationStatus.Failed;
        operation.FailureReason = reason;
        operation.CompletedAt = now;
        operation.UpdatedAt = now;
        track?.MarkFailed(reason, now);

        await db.SaveChangesAsync(cancellationToken);
    }
}
