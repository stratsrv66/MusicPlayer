using MusicPlatform.Application.Common;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>
/// Validation des fichiers audio envoyés : extension, type MIME, taille et signature binaire.
/// La vérification de la signature évite qu'un fichier arbitraire soit accepté au seul motif
/// que son extension ou son en-tête <c>Content-Type</c> sont corrects.
/// </summary>
public static class AudioFileValidator
{
    /// <summary>Nombre d'octets lus en tête de fichier pour reconnaître le format.</summary>
    public const int SignatureLength = 16;

    private static readonly Dictionary<string, string> ContentTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".flac"] = "audio/flac",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".wav"] = "audio/wav",
    };

    /// <summary>Extensions acceptées pour un fichier audio.</summary>
    public static IReadOnlyCollection<string> AllowedExtensions => ContentTypeByExtension.Keys;

    /// <summary>Retourne le type MIME canonique associé à une extension, ou <c>null</c>.</summary>
    public static string? ResolveContentType(string extension) =>
        ContentTypeByExtension.TryGetValue(extension, out var contentType) ? contentType : null;

    /// <summary>
    /// Valide le nom et la taille du fichier avant toute écriture sur disque.
    /// Retourne l'extension normalisée en minuscules.
    /// </summary>
    public static string ValidateNameAndSize(string fileName, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InputValidationException("file", "A file name is required.");
        }

        if (sizeBytes <= 0)
        {
            throw new InputValidationException("file", "The uploaded file is empty.");
        }

        if (sizeBytes > Track.MaxAudioFileSizeBytes)
        {
            throw new PayloadTooLargeException(
                $"The audio file exceeds the maximum size of {Track.MaxAudioFileSizeBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ContentTypeByExtension.ContainsKey(extension))
        {
            throw new UnsupportedMediaTypeException(
                $"Unsupported audio format '{extension}'. Allowed formats: {string.Join(", ", ContentTypeByExtension.Keys)}.");
        }

        return extension;
    }

    /// <summary>
    /// Vérifie que les premiers octets correspondent bien à un conteneur audio connu.
    /// <paramref name="header"/> doit contenir au moins <see cref="SignatureLength"/> octets
    /// lorsque le fichier est assez long.
    /// </summary>
    public static bool HasKnownAudioSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
        {
            return false;
        }

        // ID3v2 : conteneur de tags précédant un flux MPEG.
        if (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
        {
            return true;
        }

        // Synchronisation de trame MPEG (MP3/AAC ADTS) : 11 bits à 1.
        if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            return true;
        }

        // FLAC natif.
        if (header[0] == 0x66 && header[1] == 0x4C && header[2] == 0x61 && header[3] == 0x43)
        {
            return true;
        }

        // Conteneur Ogg (Vorbis, Opus).
        if (header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53)
        {
            return true;
        }

        if (header.Length < 12)
        {
            return false;
        }

        // RIFF/WAVE.
        var isRiff = header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46;
        var isWave = header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45;
        if (isRiff && isWave)
        {
            return true;
        }

        // Conteneur ISO-BMFF (M4A/MP4) : la boîte 'ftyp' commence à l'octet 4.
        return header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70;
    }

    /// <summary>Lève une erreur explicite si la signature du fichier n'est pas reconnue.</summary>
    public static void EnsureAudioSignature(ReadOnlySpan<byte> header)
    {
        if (!HasKnownAudioSignature(header))
        {
            throw new UnprocessableException(
                ErrorCodes.TrackUploadInvalid,
                "The uploaded file does not look like a supported audio file.");
        }
    }
}

/// <summary>Validation des images de pochette et d'avatar.</summary>
public static class ImageFileValidator
{
    /// <summary>Taille maximale acceptée pour une image, en octets (5 Mo).</summary>
    public const long MaxImageSizeBytes = 5L * 1024 * 1024;

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"];

    /// <summary>Valide le nom et la taille d'une image avant lecture complète en mémoire.</summary>
    public static void Validate(string fileName, long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            throw new InputValidationException("file", "The uploaded image is empty.");
        }

        if (sizeBytes > MaxImageSizeBytes)
        {
            throw new PayloadTooLargeException(
                $"The image exceeds the maximum size of {MaxImageSizeBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (Array.IndexOf(AllowedExtensions, extension) < 0)
        {
            throw new UnsupportedMediaTypeException(
                $"Unsupported image format '{extension}'. Allowed formats: {string.Join(", ", AllowedExtensions)}.");
        }
    }
}
