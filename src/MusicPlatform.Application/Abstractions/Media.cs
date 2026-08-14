using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Abstractions;

/// <summary>Métadonnées lues dans le fichier audio d'origine.</summary>
public sealed record AudioMetadata
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? Genre { get; init; }
    public int? Year { get; init; }

    /// <summary>Durée en secondes, arrondie à l'entier supérieur.</summary>
    public int DurationSeconds { get; init; }

    public int? Bitrate { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    public string? Codec { get; init; }

    /// <summary>Pochette embarquée, ou <c>null</c> si le fichier n'en contient pas.</summary>
    public byte[]? EmbeddedCover { get; init; }
}

/// <summary>Lecture des métadonnées d'un fichier audio.</summary>
public interface IAudioMetadataExtractor
{
    /// <summary>
    /// Analyse le fichier audio situé à <paramref name="relativePath"/> dans le stockage.
    /// Retourne <c>null</c> si le contenu n'est pas un fichier audio exploitable.
    /// </summary>
    Task<AudioMetadata?> ExtractAsync(string relativePath, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>Image redimensionnée produite par le traitement des pochettes.</summary>
/// <param name="Size">Taille logique générée.</param>
/// <param name="Bytes">Contenu encodé en WebP.</param>
/// <param name="Width">Largeur réelle en pixels.</param>
/// <param name="Height">Hauteur réelle en pixels.</param>
public readonly record struct ResizedImage(CoverSize Size, byte[] Bytes, int Width, int Height);

/// <summary>Traitement des images de pochette et d'avatar.</summary>
public interface IImageProcessor
{
    /// <summary>
    /// Génère les déclinaisons carrées d'une pochette (small, medium, large) au format WebP.
    /// Lève une <c>ValidationException</c> applicative si le contenu n'est pas une image valide.
    /// </summary>
    IReadOnlyList<ResizedImage> CreateCoverVariants(byte[] source);

    /// <summary>Génère une image carrée unique de la taille demandée, au format WebP.</summary>
    ResizedImage CreateSquare(byte[] source, int edgeSizePixels);
}

/// <summary>
/// File de travaux exécutés hors du cycle de la requête HTTP.
/// L'abstraction permet de remplacer l'exécution in-process par une file dédiée sans
/// modifier les cas d'utilisation.
/// </summary>
public interface IBackgroundJobQueue
{
    /// <summary>Enregistre un travail de traitement du fichier audio d'un morceau.</summary>
    ValueTask EnqueueTrackProcessingAsync(Guid uploadOperationId, CancellationToken cancellationToken = default);

    /// <summary>Enregistre un travail de génération d'archive d'export.</summary>
    ValueTask EnqueueUserExportAsync(Guid exportId, CancellationToken cancellationToken = default);
}
