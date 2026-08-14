using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;

namespace MusicPlatform.Infrastructure.Media;

/// <summary>
/// Extraction des métadonnées audio via TagLib#.
/// La lecture se fait à travers une abstraction de flux, ce qui évite de dépendre du
/// chemin physique du stockage et fonctionnera donc aussi avec un stockage objet.
/// </summary>
public sealed class TagLibAudioMetadataExtractor(IFileStorage storage, ILogger<TagLibAudioMetadataExtractor> logger)
    : IAudioMetadataExtractor
{
    /// <inheritdoc />
    public async Task<AudioMetadata?> ExtractAsync(string relativePath, string fileName, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(relativePath, cancellationToken);

        try
        {
            using var file = TagLib.File.Create(new StreamFileAbstraction(fileName, stream));
            var tag = file.Tag;
            var properties = file.Properties;

            var duration = properties is null ? 0 : (int)Math.Ceiling(properties.Duration.TotalSeconds);
            if (duration <= 0)
            {
                logger.LogWarning("File {File} has no readable audio duration.", fileName);
                return null;
            }

            return new AudioMetadata
            {
                Title = Clean(tag?.Title),
                Artist = Clean(tag?.FirstPerformer ?? tag?.FirstAlbumArtist),
                Album = Clean(tag?.Album),
                Genre = Clean(tag?.FirstGenre),
                Year = tag?.Year is > 0 and < 3000 ? (int)tag.Year : null,
                DurationSeconds = duration,
                Bitrate = properties?.AudioBitrate,
                SampleRate = properties?.AudioSampleRate,
                Channels = properties?.AudioChannels,
                Codec = properties?.Description,
                EmbeddedCover = ExtractCover(tag),
            };
        }
        catch (Exception exception) when (exception is TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            logger.LogWarning(exception, "File {File} could not be parsed as audio.", fileName);
            return null;
        }
    }

    /// <summary>Retourne la première pochette embarquée exploitable, ou <c>null</c>.</summary>
    private static byte[]? ExtractCover(TagLib.Tag? tag)
    {
        var pictures = tag?.Pictures;
        if (pictures is null || pictures.Length == 0)
        {
            return null;
        }

        foreach (var picture in pictures)
        {
            var data = picture.Data?.Data;
            if (data is { Length: > 0 })
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>Normalise une valeur de tag en supprimant les chaînes vides.</summary>
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>Adaptateur permettant à TagLib# de lire un flux plutôt qu'un fichier du système.</summary>
internal sealed class StreamFileAbstraction(string name, Stream stream) : TagLib.File.IFileAbstraction
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public Stream ReadStream { get; } = stream;

    /// <inheritdoc />
    public Stream WriteStream => throw new NotSupportedException("Metadata is never written back to the source file.");

    /// <inheritdoc />
    public void CloseStream(Stream stream)
    {
        // Le flux appartient à l'appelant, qui le libère lui-même.
    }
}
