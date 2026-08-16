using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Features.Tracks;

namespace MusicPlatform.Infrastructure.Media;

/// <summary>
/// Implémentation de <see cref="IAudioDownloader"/> déléguant à <c>yt-dlp</c>.
///
/// Chaque téléchargement dispose de son propre dossier de travail, effacé par l'appelant
/// via <see cref="DownloadedAudio.Dispose"/>. Le lancement du processus lui-même est
/// assuré par <see cref="YtDlpProcessRunner"/>.
/// </summary>
public sealed class YtDlpAudioDownloader(YtDlpProcessRunner runner, ILogger<YtDlpAudioDownloader> logger)
    : IAudioDownloader
{
    /// <summary>Nom de base des fichiers produits, l'extension étant choisie par yt-dlp.</summary>
    private const string OutputTemplate = "source.%(ext)s";

    /// <summary>Extensions de miniature que yt-dlp peut produire.</summary>
    private static readonly string[] ThumbnailExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private YtDlpOptions Options => runner.Options;

    /// <inheritdoc />
    public async Task<DownloadedAudio> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var workingDirectory = CreateWorkingDirectory();

        try
        {
            var metadataJson = await RunAsync(url, workingDirectory, cancellationToken);
            return BuildResult(workingDirectory, metadataJson);
        }
        catch
        {
            TryDeleteDirectory(workingDirectory);
            throw;
        }
    }

    /// <summary>Crée un dossier de travail isolé pour un téléchargement.</summary>
    private string CreateWorkingDirectory()
    {
        var root = string.IsNullOrWhiteSpace(Options.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "musicplatform-import")
            : Options.WorkingDirectory;

        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Exécute yt-dlp et retourne la ligne JSON décrivant la vidéo téléchargée.</summary>
    private async Task<string> RunAsync(string url, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            BuildArguments(url, workingDirectory),
            Options.TimeoutSeconds,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            logger.LogWarning("yt-dlp exited with code {ExitCode}: {Error}", result.ExitCode, result.StandardError);
            throw new UnprocessableException(
                ErrorCodes.TrackImportFailed,
                "The audio could not be downloaded. It may be unavailable, private or restricted.");
        }

        var metadataJson = YtDlpJson.FirstObjectLine(result.StandardOutput);
        if (metadataJson is null)
        {
            // yt-dlp sort en succès lorsque le filtre écarte la vidéo, sans rien produire.
            logger.LogInformation("yt-dlp produced no result for {Url}: {Error}", url, result.StandardError);
            throw new UnprocessableException(
                ErrorCodes.TrackImportFailed,
                $"The audio was not downloaded. The source may be unavailable, or longer than {Options.MaxDurationSeconds / 60} minutes.");
        }

        return metadataJson;
    }

    /// <summary>
    /// Construit la ligne de commande. <c>--no-simulate --dump-json</c> demande à la fois le
    /// téléchargement et la description JSON du média, ce qui évite un second appel.
    /// </summary>
    private List<string> BuildArguments(string url, string workingDirectory)
    {
        var arguments = new List<string>();
        runner.AddCommonArguments(arguments);

        arguments.AddRange(
        [
            "--no-playlist",
            "--format", "bestaudio/best",
            "--extract-audio",
            "--audio-format", Options.AudioFormat,
            "--audio-quality", Options.AudioQuality,
            // La miniature est conservée dans son format d'origine : ImageSharp décode
            // aussi bien le WebP servi par YouTube que le JPEG, et éviter une conversion
            // supplémentaire retire une cause d'échec sur un élément facultatif.
            "--write-thumbnail",
            "--match-filter", $"duration <= {Options.MaxDurationSeconds.ToString(CultureInfo.InvariantCulture)}",
            "--paths", workingDirectory,
            "--output", OutputTemplate,
            "--no-simulate",
            "--dump-json",
        ]);

        // Le séparateur garantit qu'une URL commençant par un tiret ne soit jamais lue
        // comme une option supplémentaire.
        arguments.Add("--");
        arguments.Add(url);

        return arguments;
    }

    /// <summary>Assemble le résultat à partir des fichiers produits et des métadonnées JSON.</summary>
    private DownloadedAudio BuildResult(string workingDirectory, string metadataJson)
    {
        using var document = JsonDocument.Parse(metadataJson);
        var root = document.RootElement;

        var audioPath = FindAudioFile(workingDirectory)
            ?? throw new UnprocessableException(
                ErrorCodes.TrackImportFailed,
                "The audio track could not be extracted. Check that ffmpeg is installed on the server.");

        var extension = Path.GetExtension(audioPath);
        var title = YtDlpJson.ReadString(root, "track") ?? YtDlpJson.ReadString(root, "title");
        var artist = YtDlpJson.ReadString(root, "artist")
                     ?? YtDlpJson.ReadString(root, "uploader")
                     ?? YtDlpJson.ReadString(root, "channel");

        return new DownloadedAudio(workingDirectory, audioPath)
        {
            FileName = BuildFileName(title, YtDlpJson.ReadString(root, "id"), extension),
            ContentType = AudioFileValidator.ResolveContentType(extension) ?? "application/octet-stream",
            SizeBytes = new FileInfo(audioPath).Length,
            Title = title,
            Artist = artist,
            Year = YtDlpJson.ReadYear(root),
            DurationSeconds = YtDlpJson.ReadDuration(root),
            Thumbnail = ReadThumbnail(workingDirectory),
        };
    }

    /// <summary>
    /// Retourne le fichier audio produit, reconnu à son extension. Le format demandé est
    /// prioritaire : yt-dlp peut laisser un fichier intermédiaire à côté du résultat final.
    /// </summary>
    private string? FindAudioFile(string workingDirectory)
    {
        var candidates = Directory.EnumerateFiles(workingDirectory)
            .Where(path => AudioFileValidator.ResolveContentType(Path.GetExtension(path)) is not null)
            .ToList();

        var expected = "." + Options.AudioFormat.TrimStart('.');

        return candidates.FirstOrDefault(path =>
                   Path.GetExtension(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    /// <summary>
    /// Lit la miniature écrite à côté du fichier audio. Une image absente ou trop
    /// volumineuse est ignorée : le morceau reste importable sans pochette.
    /// </summary>
    private byte[]? ReadThumbnail(string workingDirectory)
    {
        var path = Directory.EnumerateFiles(workingDirectory)
            .FirstOrDefault(candidate => ThumbnailExtensions.Contains(
                Path.GetExtension(candidate), StringComparer.OrdinalIgnoreCase));

        if (path is null)
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > ImageFileValidator.MaxImageSizeBytes)
        {
            logger.LogInformation("The downloaded thumbnail was ignored ({Size} bytes).", info.Length);
            return null;
        }

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// Compose un nom de fichier sûr à partir du titre du média. Le nom sert de valeur
    /// de repli pour le titre du morceau et n'est jamais utilisé comme chemin de stockage.
    /// </summary>
    private static string BuildFileName(string? title, string? sourceId, string extension)
    {
        var source = string.IsNullOrWhiteSpace(title) ? sourceId ?? "import" : title;
        var cleaned = new string(source
            .Select(character => Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? ' ' : character)
            .ToArray())
            .Trim();

        if (cleaned.Length > 100)
        {
            cleaned = cleaned[..100].Trim();
        }

        return (string.IsNullOrWhiteSpace(cleaned) ? "import" : cleaned) + extension;
    }

    /// <summary>Efface un dossier de travail en ignorant les erreurs de suppression.</summary>
    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The import working directory {Directory} could not be removed.", directory);
        }
    }
}

/// <summary>Lecture des documents JSON produits par yt-dlp.</summary>
internal static class YtDlpJson
{
    /// <summary>Retourne la première ligne exploitable comme objet JSON.</summary>
    public static string? FirstObjectLine(string output) =>
        EnumerateObjectLines(output).FirstOrDefault();

    /// <summary>Énumère les lignes exploitables comme objets JSON, dans l'ordre de sortie.</summary>
    public static IEnumerable<string> EnumerateObjectLines(string output) =>
        output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('{') && line.EndsWith('}'));

    /// <summary>Lit une propriété texte non vide du document JSON.</summary>
    public static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Lit la durée annoncée, arrondie à la seconde supérieure.</summary>
    public static int ReadDuration(JsonElement root) =>
        root.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number
            ? (int)Math.Ceiling(duration.GetDouble())
            : 0;

    /// <summary>
    /// Lit l'année de publication, exposée soit directement, soit dans la date de mise
    /// en ligne au format <c>AAAAMMJJ</c>.
    /// </summary>
    public static int? ReadYear(JsonElement root)
    {
        if (root.TryGetProperty("release_year", out var releaseYear) && releaseYear.ValueKind == JsonValueKind.Number)
        {
            return releaseYear.GetInt32();
        }

        var uploadDate = ReadString(root, "upload_date");
        if (uploadDate is { Length: >= 4 }
            && int.TryParse(uploadDate[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return year;
        }

        return null;
    }
}
