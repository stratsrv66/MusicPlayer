using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Import;

/// <summary>
/// Exécute un import de playlist, hors du cycle de la requête HTTP.
///
/// Les morceaux sont traités **un par un**, et chacun emprunte la fonction d'import d'un
/// lien YouTube isolé, <see cref="TrackImportService.ImportForOwnerAsync"/> : un import de
/// playlist n'est donc rien d'autre qu'une suite d'imports unitaires.
///
/// Chaque morceau est mené jusqu'au bout avant de passer au suivant :
///   1. rapprochement avec la bibliothèque — un morceau déjà présent n'est pas retéléchargé ;
///   2. import de la vidéo par yt-dlp ;
///   3. traitement immédiat du fichier, pour que le morceau soit écoutable sans attendre ;
///   4. ajout à la playlist, une fois le morceau réellement disponible.
///
/// Le traitement est déclenché ici plutôt que laissé à la file de travaux : celle-ci est
/// consommée séquentiellement, et l'import occupant son unique consommateur, les morceaux
/// seraient restés en attente de traitement jusqu'à la fin de l'import complet.
///
/// L'état de chaque morceau est persisté au fil de l'eau : un arrêt du serveur ne fait
/// perdre qu'un morceau, et la reprise repart de ceux restés en attente.
/// </summary>
public sealed class PlaylistImportRunner(
    IAppDbContext db,
    TrackImportService trackImportService,
    TrackProcessingService trackProcessingService,
    TrackMatcher matcher,
    IClock clock,
    ILogger<PlaylistImportRunner> logger)
{
    /// <summary>
    /// Traite un import jusqu'à son terme. La méthode est idempotente : un import déjà
    /// terminé est ignoré, et un import repris ne retraite que ses morceaux en attente.
    /// </summary>
    public async Task ProcessAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await db.PlaylistImports.FirstOrDefaultAsync(i => i.Id == importId, cancellationToken);

        if (import is null || import.IsTerminal)
        {
            logger.LogDebug("Playlist import {ImportId} is not pending processing.", importId);
            return;
        }

        try
        {
            await RunAsync(import, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // L'arrêt de l'application laisse l'import reprenable : aucun état terminal.
            logger.LogInformation("Playlist import {ImportId} was interrupted and will resume.", importId);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Playlist import {ImportId} failed.", importId);

            import.Status = PlaylistImportStatus.Failed;
            import.FailureReason = "The import could not be completed.";
            import.CompletedAt = clock.UtcNow;
            import.UpdatedAt = import.CompletedAt.Value;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Traite les morceaux en attente, un par un, jusqu'à épuisement.</summary>
    private async Task RunAsync(PlaylistImport import, CancellationToken cancellationToken)
    {
        import.Status = PlaylistImportStatus.Running;
        import.StartedAt ??= clock.UtcNow;
        import.UpdatedAt = clock.UtcNow;

        // Un morceau laissé « en cours » par un arrêt brutal doit être retraité.
        await ResetRunningItemsAsync(import.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var playlist = await LoadMirrorPlaylistAsync(import, cancellationToken);

        // La boucle se termine lorsqu'il ne reste aucun morceau en attente, ou sur
        // annulation : sa condition de sortie est bornée par le nombre de morceaux.
        while (true)
        {
            if (await IsCancelledAsync(import.Id, cancellationToken))
            {
                await CancelRemainingAsync(import, cancellationToken);
                return;
            }

            var item = await db.PlaylistImportItems
                .Where(i => i.ImportId == import.Id && i.Status == PlaylistImportItemStatus.Pending)
                .OrderBy(i => i.Position)
                .FirstOrDefaultAsync(cancellationToken);

            if (item is null)
            {
                break;
            }

            await ProcessItemAsync(import, playlist, item, cancellationToken);
        }

        import.Status = PlaylistImportStatus.Completed;
        import.CompletedAt = clock.UtcNow;
        import.UpdatedAt = import.CompletedAt.Value;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Playlist import {ImportId} completed.", import.Id);
    }

    /// <summary>
    /// Mène un morceau de bout en bout. Toute erreur est consignée sur le morceau plutôt
    /// que propagée : un échec isolé ne doit pas interrompre l'import.
    /// </summary>
    private async Task ProcessItemAsync(
        PlaylistImport import,
        Playlist? playlist,
        PlaylistImportItem item,
        CancellationToken cancellationToken)
    {
        item.Attempts++;
        item.Status = PlaylistImportItemStatus.Running;
        item.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var identity = BuildIdentity(item);
            var existing = await matcher.FindAsync(import.UserId, identity, cancellationToken);

            if (existing is not null)
            {
                await matcher.ApplyIdentityAsync(existing, identity, cancellationToken);
                item.TrackId = existing.Id;
                item.Status = PlaylistImportItemStatus.Duplicate;
            }
            else
            {
                item.TrackId = await ImportTrackAsync(import, item, cancellationToken);
                item.Status = PlaylistImportItemStatus.Imported;
            }

            item.FailureReason = null;

            // Le morceau n'est ajouté à la playlist qu'une fois réellement disponible.
            await AddToPlaylistAsync(playlist, item.TrackId.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // L'arrêt de l'application remet simplement le morceau en attente.
            item.Status = PlaylistImportItemStatus.Pending;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Track '{Title}' could not be imported.", item.Title);
            item.Status = PlaylistImportItemStatus.Failed;
            item.FailureReason = Truncate(exception.Message);
        }

        item.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Importe la vidéo puis en traite immédiatement le fichier, de sorte que le morceau
    /// soit écoutable dès son ajout à la playlist.
    /// </summary>
    private async Task<Guid> ImportTrackAsync(
        PlaylistImport import,
        PlaylistImportItem item,
        CancellationToken cancellationToken)
    {
        var url = item.SourceUrl ?? $"https://www.youtube.com/watch?v={item.SourceTrackId}";

        var accepted = await trackImportService.ImportForOwnerAsync(
            import.UserId,
            new ImportYoutubeTrackRequest { Url = url, Visibility = import.Visibility },
            cancellationToken);

        await trackProcessingService.ProcessAsync(accepted.UploadOperationId, cancellationToken);

        var status = await db.Tracks
            .Where(t => t.Id == accepted.TrackId)
            .Select(t => t.Status)
            .FirstAsync(cancellationToken);

        if (status != TrackStatus.Ready)
        {
            throw new InvalidOperationException("The downloaded file could not be processed.");
        }

        return accepted.TrackId;
    }

    /// <summary>Charge la playlist de la bibliothèque reflétant celle importée, si elle existe.</summary>
    private async Task<Playlist?> LoadMirrorPlaylistAsync(PlaylistImport import, CancellationToken cancellationToken) =>
        import.PlaylistId is { } playlistId
            ? await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId, cancellationToken)
            : null;

    /// <summary>
    /// Ajoute un morceau à la playlist miroir, à la suite des morceaux déjà présents.
    /// Un morceau figurant deux fois dans la playlist d'origine n'est ajouté qu'une fois.
    /// </summary>
    private async Task AddToPlaylistAsync(Playlist? playlist, Guid trackId, CancellationToken cancellationToken)
    {
        if (playlist is null)
        {
            return;
        }

        var alreadyPresent = await db.PlaylistItems
            .AnyAsync(i => i.PlaylistId == playlist.Id && i.TrackId == trackId, cancellationToken);

        if (alreadyPresent)
        {
            return;
        }

        var count = await db.PlaylistItems.CountAsync(i => i.PlaylistId == playlist.Id, cancellationToken);

        if (count >= Playlist.MaxItems)
        {
            return;
        }

        db.PlaylistItems.Add(new PlaylistItem
        {
            PlaylistId = playlist.Id,
            TrackId = trackId,
            Position = count,
            AddedAt = clock.UtcNow,
        });

        playlist.UpdatedAt = clock.UtcNow;
    }

    /// <summary>Remet en attente les morceaux laissés « en cours » par un arrêt brutal.</summary>
    private async Task ResetRunningItemsAsync(Guid importId, CancellationToken cancellationToken)
    {
        var stalled = await db.PlaylistImportItems
            .Where(i => i.ImportId == importId && i.Status == PlaylistImportItemStatus.Running)
            .ToListAsync(cancellationToken);

        foreach (var item in stalled)
        {
            item.Status = PlaylistImportItemStatus.Pending;
            item.UpdatedAt = clock.UtcNow;
        }
    }

    /// <summary>Relit l'état de l'import en base afin de détecter une annulation demandée.</summary>
    private async Task<bool> IsCancelledAsync(Guid importId, CancellationToken cancellationToken) =>
        await db.PlaylistImports
            .Where(i => i.Id == importId)
            .Select(i => i.Status)
            .FirstOrDefaultAsync(cancellationToken) == PlaylistImportStatus.Cancelled;

    /// <summary>Clôt un import annulé en marquant les morceaux non traités.</summary>
    private async Task CancelRemainingAsync(PlaylistImport import, CancellationToken cancellationToken)
    {
        var remaining = await db.PlaylistImportItems
            .Where(i => i.ImportId == import.Id
                        && (i.Status == PlaylistImportItemStatus.Pending || i.Status == PlaylistImportItemStatus.Running))
            .ToListAsync(cancellationToken);

        foreach (var item in remaining)
        {
            item.Status = PlaylistImportItemStatus.Cancelled;
            item.UpdatedAt = clock.UtcNow;
        }

        import.CompletedAt = clock.UtcNow;
        import.UpdatedAt = import.CompletedAt.Value;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Playlist import {ImportId} was cancelled.", import.Id);
    }

    /// <summary>Assemble les éléments d'identification d'un morceau inventorié.</summary>
    private static TrackIdentity BuildIdentity(PlaylistImportItem item) =>
        new(ExternalPlatform.Youtube, item.SourceTrackId, item.ArtistName, item.Title, item.DurationSeconds);

    /// <summary>Borne un message d'erreur à la longueur acceptée par la colonne.</summary>
    private static string Truncate(string message) =>
        message.Length <= 500 ? message : message[..500];
}
