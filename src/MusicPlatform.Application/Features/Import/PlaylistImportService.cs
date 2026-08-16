using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Import;

/// <summary>
/// Cas d'utilisation de l'import de playlists YouTube : aperçu d'une playlist, lancement,
/// suivi, annulation et relance des morceaux en échec.
///
/// Le service ne télécharge rien : il constitue l'inventaire puis confie l'exécution à
/// <see cref="PlaylistImportRunner"/> via la file de travaux, afin que la requête HTTP
/// ne porte pas le coût de l'import.
/// </summary>
public sealed class PlaylistImportService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPlaylistProvider provider,
    IBackgroundJobQueue jobs,
    IClock clock,
    ILogger<PlaylistImportService> logger)
{
    /// <summary>Nombre d'imports retournés dans l'historique de l'utilisateur.</summary>
    private const int HistorySize = 20;

    /// <summary>
    /// Décrit une playlist et ses morceaux sans rien persister, afin que l'utilisateur
    /// puisse vérifier le contenu et le nombre de morceaux avant de lancer l'import.
    /// </summary>
    public async Task<PlaylistPreviewDto> PreviewAsync(string? urlOrId, CancellationToken cancellationToken)
    {
        var playlistId = ParsePlaylistId(urlOrId);

        var playlist = await provider.GetPlaylistAsync(playlistId, cancellationToken);
        var tracks = await provider.GetTracksAsync(playlistId, cancellationToken);

        return new PlaylistPreviewDto(ToDto(playlist), [.. tracks.Select(ToDto)]);
    }

    /// <summary>Liste les playlists publiques d'une chaîne YouTube.</summary>
    public async Task<IReadOnlyList<ExternalPlaylistDto>> ListProfilePlaylistsAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var playlists = await provider.ListProfilePlaylistsAsync(profileId, cancellationToken);
        return [.. playlists.Select(ToDto)];
    }

    /// <summary>
    /// Inventorie la playlist puis programme son import. Les morceaux sont figés en base
    /// dès maintenant : la progression est donc consultable immédiatement, et l'import
    /// reste reprenable sans réinterroger YouTube.
    /// </summary>
    public async Task<PlaylistImportDto> StartAsync(StartPlaylistImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var playlistId = ParsePlaylistId(request.Url);

        await EnsureNoRunningImportAsync(userId, playlistId, cancellationToken);

        var playlist = await provider.GetPlaylistAsync(playlistId, cancellationToken);
        var tracks = await provider.GetTracksAsync(playlistId, cancellationToken);

        if (tracks.Count == 0)
        {
            throw new UnprocessableException(
                ErrorCodes.PlaylistImportUnreadable,
                "This playlist contains no importable track.");
        }

        if (tracks.Count > PlaylistImport.MaxTracks)
        {
            throw new UnprocessableException(
                ErrorCodes.PlaylistImportTooLarge,
                $"This playlist holds {tracks.Count} tracks, beyond the limit of {PlaylistImport.MaxTracks}.");
        }

        var now = clock.UtcNow;
        var import = new PlaylistImport
        {
            UserId = userId,
            Platform = ExternalPlatform.Youtube,
            SourcePlaylistId = playlistId,
            SourceUrl = playlist.Url,
            Name = playlist.Name,
            TotalTracks = tracks.Count,
            Status = PlaylistImportStatus.Pending,
            Visibility = request.Visibility,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (request.CreatePlaylist)
        {
            import.Playlist = CreateMirrorPlaylist(userId, playlist, request.Visibility, now);
            db.Playlists.Add(import.Playlist);
        }

        for (var position = 0; position < tracks.Count; position++)
        {
            import.Items.Add(ToItem(tracks[position], position, now));
        }

        db.PlaylistImports.Add(import);
        await db.SaveChangesAsync(cancellationToken);

        await jobs.EnqueuePlaylistImportAsync(import.Id, cancellationToken);

        logger.LogInformation("Playlist import {ImportId} queued: {Count} tracks.", import.Id, tracks.Count);

        return await GetDtoAsync(import.Id, cancellationToken);
    }

    /// <summary>Retourne un import et le détail de ses morceaux.</summary>
    public async Task<PlaylistImportDetailsDto> GetAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await LoadOwnedAsync(importId, cancellationToken);

        var items = await db.PlaylistImportItems
            .AsNoTracking()
            .Where(i => i.ImportId == importId)
            .OrderBy(i => i.Position)
            .Select(i => new PlaylistImportItemDto(
                i.Id,
                i.Position,
                i.Title,
                i.ArtistName,
                i.DurationSeconds,
                i.Status,
                i.FailureReason,
                i.Attempts,
                i.TrackId))
            .ToListAsync(cancellationToken);

        return new PlaylistImportDetailsDto(ToDto(import, BuildProgress(items)), items);
    }

    /// <summary>Retourne les imports les plus récents de l'utilisateur.</summary>
    public async Task<IReadOnlyList<PlaylistImportDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var imports = await db.PlaylistImports
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(HistorySize)
            .ToListAsync(cancellationToken);

        var identifiers = imports.Select(i => i.Id).ToList();

        // Un seul regroupement pour tous les imports listés, plutôt qu'une requête par import.
        var counts = await db.PlaylistImportItems
            .AsNoTracking()
            .Where(i => identifiers.Contains(i.ImportId))
            .GroupBy(i => new { i.ImportId, i.Status })
            .Select(group => new { group.Key.ImportId, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return
        [
            .. imports.Select(import => ToDto(
                import,
                BuildProgress(counts.Where(c => c.ImportId == import.Id).ToDictionary(c => c.Status, c => c.Count)))),
        ];
    }

    /// <summary>
    /// Demande l'annulation d'un import. Le morceau en cours de téléchargement va à son
    /// terme ; les suivants sont abandonnés.
    /// </summary>
    public async Task<PlaylistImportDto> CancelAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await LoadOwnedAsync(importId, cancellationToken);

        if (import.IsTerminal)
        {
            throw new UnprocessableException(
                ErrorCodes.PlaylistImportAlreadyRunning,
                "This import is already finished.");
        }

        import.Status = PlaylistImportStatus.Cancelled;
        import.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await GetDtoAsync(importId, cancellationToken);
    }

    /// <summary>
    /// Remet en attente les morceaux en échec ou annulés, puis reprogramme l'import.
    /// Les morceaux déjà importés ne sont jamais retraités.
    /// </summary>
    public async Task<PlaylistImportDto> RetryFailedAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await LoadOwnedAsync(importId, cancellationToken);

        var retryable = await db.PlaylistImportItems
            .Where(i => i.ImportId == importId
                        && (i.Status == PlaylistImportItemStatus.Failed
                            || i.Status == PlaylistImportItemStatus.Cancelled))
            .ToListAsync(cancellationToken);

        if (retryable.Count == 0)
        {
            throw new UnprocessableException(
                ErrorCodes.PlaylistImportUnreadable,
                "This import has no track to retry.");
        }

        foreach (var item in retryable)
        {
            item.Status = PlaylistImportItemStatus.Pending;
            item.FailureReason = null;
            item.UpdatedAt = clock.UtcNow;
        }

        import.Status = PlaylistImportStatus.Pending;
        import.CompletedAt = null;
        import.FailureReason = null;
        import.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await jobs.EnqueuePlaylistImportAsync(import.Id, cancellationToken);

        logger.LogInformation("Playlist import {ImportId} requeued for {Count} tracks.", importId, retryable.Count);

        return await GetDtoAsync(importId, cancellationToken);
    }

    /// <summary>Construit la playlist de la bibliothèque reflétant la playlist importée.</summary>
    private static Playlist CreateMirrorPlaylist(
        Guid userId,
        ExternalPlaylist source,
        ContentVisibility visibility,
        DateTime now) =>
        new()
        {
            OwnerId = userId,
            Name = Shorten(source.Name, 200),
            Description = $"Importée depuis YouTube — {source.Url}",
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>Convertit un morceau relevé en entrée d'inventaire.</summary>
    private static PlaylistImportItem ToItem(ExternalTrack track, int position, DateTime now) => new()
    {
        Position = position,
        SourceTrackId = track.SourceId,
        Title = Shorten(track.Title, 300),
        ArtistName = Shorten(track.ArtistName, 300),
        DurationSeconds = Math.Max(0, track.DurationSeconds),
        SourceUrl = track.SourceUrl,
        Status = PlaylistImportItemStatus.Pending,
        UpdatedAt = now,
    };

    /// <summary>Charge un import appartenant à l'appelant, ou lève une erreur 404.</summary>
    private async Task<PlaylistImport> LoadOwnedAsync(Guid importId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        return await db.PlaylistImports.FirstOrDefaultAsync(i => i.Id == importId && i.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.PlaylistImportNotFound, "The requested import does not exist.");
    }

    /// <summary>Recharge un import sous forme de DTO, compteurs à jour.</summary>
    private async Task<PlaylistImportDto> GetDtoAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await LoadOwnedAsync(importId, cancellationToken);

        var counts = await db.PlaylistImportItems
            .AsNoTracking()
            .Where(i => i.ImportId == importId)
            .GroupBy(i => i.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return ToDto(import, BuildProgress(counts.ToDictionary(c => c.Status, c => c.Count)));
    }

    /// <summary>Refuse un second import simultané de la même playlist.</summary>
    private async Task EnsureNoRunningImportAsync(Guid userId, string playlistId, CancellationToken cancellationToken)
    {
        var running = await db.PlaylistImports.AnyAsync(
            i => i.UserId == userId
                 && i.SourcePlaylistId == playlistId
                 && (i.Status == PlaylistImportStatus.Pending || i.Status == PlaylistImportStatus.Running),
            cancellationToken);

        if (running)
        {
            throw new ConflictException(
                ErrorCodes.PlaylistImportAlreadyRunning,
                "This playlist is already being imported.");
        }
    }

    /// <summary>Extrait l'identifiant de playlist de la saisie, ou lève une erreur de validation.</summary>
    private string ParsePlaylistId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            throw new InputValidationException("url", "A playlist link is required.");
        }

        return provider.TryParsePlaylistId(urlOrId)
            ?? throw new InputValidationException("url", "This link does not point to a YouTube playlist.");
    }

    /// <summary>Assemble les compteurs de progression à partir des états relevés.</summary>
    private static PlaylistImportProgressDto BuildProgress(IReadOnlyDictionary<PlaylistImportItemStatus, int> counts)
    {
        var total = counts.Values.Sum();
        var pending = Count(counts, PlaylistImportItemStatus.Pending);
        var running = Count(counts, PlaylistImportItemStatus.Running);

        return new PlaylistImportProgressDto(
            total,
            total - pending - running,
            pending,
            running,
            Count(counts, PlaylistImportItemStatus.Imported),
            Count(counts, PlaylistImportItemStatus.Duplicate),
            Count(counts, PlaylistImportItemStatus.Failed),
            Count(counts, PlaylistImportItemStatus.Cancelled));
    }

    /// <summary>Assemble les compteurs à partir d'une liste de morceaux déjà projetée.</summary>
    private static PlaylistImportProgressDto BuildProgress(IReadOnlyList<PlaylistImportItemDto> items) =>
        BuildProgress(items
            .GroupBy(item => item.Status)
            .ToDictionary(group => group.Key, group => group.Count()));

    private static int Count(IReadOnlyDictionary<PlaylistImportItemStatus, int> counts, PlaylistImportItemStatus status) =>
        counts.TryGetValue(status, out var value) ? value : 0;

    private static ExternalPlaylistDto ToDto(ExternalPlaylist playlist) => new(
        playlist.Id,
        playlist.Name,
        playlist.Owner,
        playlist.CoverUrl,
        playlist.TrackCount,
        playlist.Url);

    private static ExternalTrackDto ToDto(ExternalTrack track) => new(
        track.SourceId,
        track.Title,
        track.ArtistName,
        track.DurationSeconds);

    private static PlaylistImportDto ToDto(PlaylistImport import, PlaylistImportProgressDto progress) => new(
        import.Id,
        import.Name,
        import.SourceUrl,
        import.Status,
        import.Visibility,
        import.PlaylistId,
        import.FailureReason,
        progress,
        import.CreatedAt,
        import.CompletedAt);

    /// <summary>Borne une chaîne à la longueur acceptée par la colonne correspondante.</summary>
    private static string Shorten(string value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
