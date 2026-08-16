using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Features.Import;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Infrastructure.Media;

namespace MusicPlatform.Infrastructure.Providers;

/// <summary>
/// Énumération des playlists YouTube via <c>yt-dlp</c>.
///
/// L'option <c>--flat-playlist</c> liste le contenu sans télécharger le moindre média :
/// l'inventaire est donc rapide et ne consomme pas de bande passante. Le téléchargement
/// proprement dit est ensuite confié au même composant que l'import d'un morceau seul.
/// </summary>
public sealed partial class YoutubePlaylistProvider(
    YtDlpProcessRunner runner,
    ILogger<YoutubePlaylistProvider> logger) : IPlaylistProvider
{
    /// <inheritdoc />
    public string? TryParsePlaylistId(string urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            return null;
        }

        var candidate = urlOrId.Trim();

        // Identifiant collé directement.
        if (PlaylistIdPattern().IsMatch(candidate))
        {
            return candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url))
        {
            return null;
        }

        var host = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.Host[4..] : url.Host;
        if (!host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && pair[..separator] == "list")
            {
                var id = Uri.UnescapeDataString(pair[(separator + 1)..]);
                return PlaylistIdPattern().IsMatch(id) ? id : null;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<ExternalPlaylist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        using var document = await LoadAsync(BuildPlaylistUrl(playlistId), 1, cancellationToken);
        return ReadPlaylist(document.RootElement, playlistId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalTrack>> GetTracksAsync(
        string playlistId,
        CancellationToken cancellationToken = default)
    {
        using var document = await LoadAsync(BuildPlaylistUrl(playlistId), PlaylistImport.MaxTracks, cancellationToken);

        if (!document.RootElement.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tracks = new List<ExternalTrack>(entries.GetArrayLength());

        foreach (var entry in entries.EnumerateArray())
        {
            var track = ReadTrack(entry);
            if (track is not null)
            {
                tracks.Add(track);
            }
        }

        return tracks;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalPlaylist>> ListProfilePlaylistsAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new InputValidationException("profileId", "A channel identifier is required.");
        }

        using var document = await LoadAsync(BuildChannelPlaylistsUrl(profileId.Trim()), 100, cancellationToken);

        if (!document.RootElement.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var playlists = new List<ExternalPlaylist>();

        foreach (var entry in entries.EnumerateArray())
        {
            var id = YtDlpJson.ReadString(entry, "id");
            if (id is not null)
            {
                playlists.Add(ReadPlaylist(entry, id));
            }
        }

        return playlists;
    }

    /// <summary>
    /// Interroge yt-dlp en mode « playlist à plat » et retourne le document décrivant la
    /// collection. Le résultat n'est pas mis en cache : l'inventaire n'a lieu qu'une fois
    /// par import.
    /// </summary>
    private async Task<JsonDocument> LoadAsync(string url, int limit, CancellationToken cancellationToken)
    {
        var arguments = new List<string>();
        runner.AddCommonArguments(arguments);

        arguments.AddRange(
        [
            "--flat-playlist",
            "--dump-single-json",
            "--playlist-end", limit.ToString(CultureInfo.InvariantCulture),
            "--",
            url,
        ]);

        var result = await runner.RunAsync(arguments, runner.Options.MetadataTimeoutSeconds, cancellationToken);

        if (result.ExitCode != 0)
        {
            logger.LogWarning(
                "yt-dlp could not read the playlist {Url} (code {ExitCode}): {Error}",
                url,
                result.ExitCode,
                result.StandardError);

            throw new UnprocessableException(
                ErrorCodes.PlaylistImportUnreadable,
                "This playlist could not be read. Check that the link is correct and that the playlist is public.");
        }

        var payload = YtDlpJson.FirstObjectLine(result.StandardOutput)
            ?? throw new UnprocessableException(
                ErrorCodes.PlaylistImportUnreadable,
                "This playlist could not be read. Check that the link is correct and that the playlist is public.");

        return JsonDocument.Parse(payload);
    }

    /// <summary>Convertit un document de playlist en descripteur applicatif.</summary>
    private static ExternalPlaylist ReadPlaylist(JsonElement element, string playlistId)
    {
        var count = 0;

        if (element.TryGetProperty("playlist_count", out var declared) && declared.ValueKind == JsonValueKind.Number)
        {
            count = declared.GetInt32();
        }
        else if (element.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            count = entries.GetArrayLength();
        }

        return new ExternalPlaylist(
            playlistId,
            YtDlpJson.ReadString(element, "title") ?? playlistId,
            YtDlpJson.ReadString(element, "uploader") ?? YtDlpJson.ReadString(element, "channel"),
            ReadThumbnailUrl(element),
            count,
            YtDlpJson.ReadString(element, "webpage_url") ?? BuildPlaylistUrl(playlistId));
    }

    /// <summary>
    /// Convertit une entrée de playlist en morceau externe, ou <c>null</c> si l'entrée est
    /// inexploitable — une vidéo supprimée ou privée reste listée mais sans titre.
    /// </summary>
    private static ExternalTrack? ReadTrack(JsonElement entry)
    {
        var sourceId = YtDlpJson.ReadString(entry, "id");
        var rawTitle = YtDlpJson.ReadString(entry, "title");

        if (sourceId is null || rawTitle is null)
        {
            return null;
        }

        var uploader = YtDlpJson.ReadString(entry, "uploader")
                       ?? YtDlpJson.ReadString(entry, "channel")
                       ?? YtDlpJson.ReadString(entry, "uploader_id");

        // YouTube n'expose pas d'artiste distinct du titre : « Artiste - Titre » est la
        // convention dominante, et la chaîne sert de repli. Ce découpage ne sert qu'au
        // rapprochement et à l'affichage de l'aperçu : les métadonnées finales du morceau
        // proviennent du téléchargement.
        var (artist, title) = MetadataNormalizer.SplitVideoTitle(rawTitle, uploader);

        return new ExternalTrack
        {
            SourceId = sourceId,
            Title = title.Length > 0 ? title : rawTitle,
            ArtistName = artist.Length > 0 ? artist : uploader ?? string.Empty,
            DurationSeconds = YtDlpJson.ReadDuration(entry),
            SourceUrl = YtDlpJson.ReadString(entry, "url")
                        ?? YtDlpJson.ReadString(entry, "webpage_url")
                        ?? $"https://www.youtube.com/watch?v={sourceId}",
        };
    }

    /// <summary>Retient la vignette la plus grande annoncée par yt-dlp.</summary>
    private static string? ReadThumbnailUrl(JsonElement element)
    {
        var direct = YtDlpJson.ReadString(element, "thumbnail");
        if (direct is not null)
        {
            return direct;
        }

        if (!element.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        var bestWidth = -1;

        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            var url = YtDlpJson.ReadString(thumbnail, "url");
            if (url is null)
            {
                continue;
            }

            var width = thumbnail.TryGetProperty("width", out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;

            if (width > bestWidth)
            {
                bestWidth = width;
                best = url;
            }
        }

        return best;
    }

    /// <summary>Construit l'URL publique d'une playlist à partir de son identifiant.</summary>
    private static string BuildPlaylistUrl(string playlistId) =>
        $"https://www.youtube.com/playlist?list={playlistId}";

    /// <summary>Construit l'URL listant les playlists publiques d'une chaîne.</summary>
    private static string BuildChannelPlaylistsUrl(string profileId)
    {
        // Accepte une URL de chaîne complète, un pseudo « @handle » ou un identifiant brut.
        if (Uri.TryCreate(profileId, UriKind.Absolute, out var url))
        {
            return $"{url.GetLeftPart(UriPartial.Path).TrimEnd('/')}/playlists";
        }

        var handle = profileId.StartsWith('@') ? profileId : "@" + profileId;
        return $"https://www.youtube.com/{handle}/playlists";
    }

    /// <summary>Identifiant de playlist YouTube : lettres, chiffres, tirets et soulignés.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{12,64}$")]
    private static partial Regex PlaylistIdPattern();
}
