using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Import;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>
/// Import de morceaux depuis YouTube.
///
/// Le service produit un fichier audio local avec yt-dlp, puis délègue à
/// <see cref="TrackService"/> : l'import emprunte ainsi exactement le même chemin de
/// validation, de stockage et de traitement qu'un envoi manuel. La miniature de la
/// vidéo est ensuite installée comme pochette du morceau.
///
/// <see cref="AddToLibraryAsync"/> est le point d'entrée partagé par l'import d'un
/// morceau seul et par l'import d'une playlist : les deux produisent donc des morceaux
/// strictement identiques.
/// </summary>
public sealed partial class TrackImportService(
    IAudioDownloader downloader,
    TrackService trackService,
    TrackCoverService coverService,
    TrackMatcher matcher,
    ICurrentUser currentUser,
    IAppDbContext db,
    ILogger<TrackImportService> logger)
{
    /// <summary>
    /// Télécharge la piste audio d'une vidéo YouTube et crée le morceau correspondant.
    /// Les métadonnées saisies par l'utilisateur priment toujours sur celles de la vidéo.
    /// </summary>
    /// <param name="request">Lien de la vidéo et métadonnées facultatives.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>L'accusé de réception de l'upload, identique à celui d'un envoi de fichier.</returns>
    public Task<UploadAcceptedDto> ImportFromYoutubeAsync(
        ImportYoutubeTrackRequest request,
        CancellationToken cancellationToken) =>
        ImportForOwnerAsync(currentUser.RequireUserId(), request, cancellationToken);

    /// <summary>
    /// Importe une vidéo YouTube au nom d'un propriétaire explicite.
    ///
    /// C'est l'unique implémentation de l'import YouTube : l'import d'un lien isolé comme
    /// celui d'une playlist passent tous deux par ici, morceau par morceau. La variante à
    /// propriétaire explicite existe parce que l'import d'une playlist s'exécute en
    /// arrière-plan, sans utilisateur courant. Le contrôle d'accès incombe à l'appelant.
    /// </summary>
    /// <param name="ownerId">Propriétaire du morceau créé.</param>
    /// <param name="request">Lien de la vidéo et métadonnées facultatives.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<UploadAcceptedDto> ImportForOwnerAsync(
        Guid ownerId,
        ImportYoutubeTrackRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var videoId = ParseVideoId(request.Url);
        var url = BuildWatchUrl(videoId);

        using var audio = await downloader.DownloadAsync(url, cancellationToken);

        var createRequest = new CreateTrackRequest
        {
            Title = Coalesce(request.Title, audio.Title),
            ArtistName = Coalesce(request.ArtistName, audio.Artist),
            Description = request.Description,
            GenreId = request.GenreId,
            Year = request.Year ?? audio.Year,
            Visibility = request.Visibility,
            Tags = request.Tags,
        };

        var file = new UploadedFile(
            audio.FileName,
            audio.ContentType,
            audio.SizeBytes,
            () => File.OpenRead(audio.FilePath));

        var accepted = await trackService.CreateForOwnerAsync(ownerId, createRequest, file, cancellationToken);

        var track = await db.Tracks.FirstAsync(t => t.Id == accepted.TrackId, cancellationToken);
        await matcher.ApplyIdentityAsync(
            track,
            new TrackIdentity(ExternalPlatform.Youtube, videoId, track.ArtistName, track.Title, audio.DurationSeconds),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        if (audio.Thumbnail is { Length: > 0 })
        {
            await ApplyThumbnailAsync(accepted.TrackId, audio.Thumbnail, cancellationToken);
        }

        logger.LogInformation("Track {TrackId} imported from {Url}.", accepted.TrackId, url);
        return accepted;
    }

    /// <summary>
    /// Installe la miniature de la vidéo comme pochette du morceau.
    ///
    /// L'échec n'est jamais propagé : une image illisible ne doit pas faire perdre un
    /// import dont le fichier audio est déjà accepté et en cours de traitement.
    /// </summary>
    private async Task ApplyThumbnailAsync(Guid trackId, byte[] thumbnail, CancellationToken cancellationToken)
    {
        try
        {
            var track = await db.Tracks.FirstAsync(t => t.Id == trackId, cancellationToken);
            await coverService.GenerateAsync(track, thumbnail, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The thumbnail of track {TrackId} could not be used as cover art.", trackId);
        }
    }

    /// <summary>
    /// Valide le lien reçu et le réduit à sa forme canonique.
    ///
    /// Seuls les domaines YouTube sont acceptés : l'URL est transmise à un outil de
    /// téléchargement qui accepterait sinon n'importe quelle adresse, y compris une
    /// ressource interne au réseau du serveur. La forme canonique écarte de plus les
    /// paramètres annexes, notamment une éventuelle playlist.
    /// </summary>
    /// <param name="url">Lien saisi par l'utilisateur.</param>
    /// <returns>Une URL de la forme <c>https://www.youtube.com/watch?v={id}</c>.</returns>
    internal static string NormalizeYoutubeUrl(string? url) => BuildWatchUrl(ParseVideoId(url));

    /// <summary>Construit l'URL canonique d'une vidéo à partir de son identifiant.</summary>
    private static string BuildWatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    /// <summary>Valide le lien et en extrait l'identifiant de vidéo.</summary>
    private static string ParseVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            throw new InputValidationException("url", "A valid YouTube link is required.");
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            throw new InputValidationException("url", "The link must use the http or https scheme.");
        }

        return ExtractVideoId(parsed)
            ?? throw new InputValidationException("url", "This link does not point to a YouTube video.");
    }

    /// <summary>Extrait l'identifiant de vidéo d'une URL YouTube, ou <c>null</c>.</summary>
    private static string? ExtractVideoId(Uri url)
    {
        var host = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.Host[4..] : url.Host;
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // youtu.be/{id} : l'identifiant est le premier segment du chemin.
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Length > 0 ? Validate(segments[0]) : null;
        }

        if (!host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // youtube.com/shorts/{id}, /embed/{id} et /v/{id} portent l'identifiant dans le chemin.
        if (segments.Length >= 2 && segments[0] is "shorts" or "embed" or "v")
        {
            return Validate(segments[1]);
        }

        // youtube.com/watch?v={id} : cas usuel.
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && pair[..separator] == "v")
            {
                return Validate(Uri.UnescapeDataString(pair[(separator + 1)..]));
            }
        }

        return null;
    }

    /// <summary>Retourne l'identifiant s'il respecte le format YouTube, sinon <c>null</c>.</summary>
    private static string? Validate(string candidate) => VideoIdPattern().IsMatch(candidate) ? candidate : null;

    /// <summary>Identifiant de vidéo YouTube : onze caractères de l'alphabet base64 URL.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();

    /// <summary>Retient la valeur saisie par l'utilisateur, ou à défaut celle de la vidéo.</summary>
    private static string? Coalesce(string? provided, string? fallback) =>
        string.IsNullOrWhiteSpace(provided) ? fallback : provided.Trim();
}
