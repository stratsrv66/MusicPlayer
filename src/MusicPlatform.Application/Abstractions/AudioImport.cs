namespace MusicPlatform.Application.Abstractions;

/// <summary>
/// Audio récupéré depuis une plateforme vidéo externe, matérialisé dans un dossier de
/// travail temporaire situé hors du stockage applicatif.
///
/// L'appelant doit libérer l'instance : <see cref="Dispose"/> efface le dossier de travail
/// et tout son contenu, y compris le fichier audio et la miniature téléchargés.
/// </summary>
/// <param name="workingDirectory">Dossier temporaire créé pour ce téléchargement.</param>
/// <param name="filePath">Chemin absolu du fichier audio produit.</param>
public sealed class DownloadedAudio(string workingDirectory, string filePath) : IDisposable
{
    /// <summary>Dossier temporaire contenant le fichier audio et la miniature.</summary>
    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>Chemin absolu du fichier audio téléchargé.</summary>
    public string FilePath { get; } = filePath;

    /// <summary>Nom de fichier proposé pour le morceau, extension comprise.</summary>
    public required string FileName { get; init; }

    /// <summary>Type MIME du fichier audio produit.</summary>
    public required string ContentType { get; init; }

    /// <summary>Taille du fichier audio en octets.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Titre annoncé par la plateforme, ou <c>null</c> s'il est inconnu.</summary>
    public string? Title { get; init; }

    /// <summary>Auteur ou chaîne d'origine, utilisable comme nom d'artiste.</summary>
    public string? Artist { get; init; }

    /// <summary>Année de publication, ou <c>null</c> si elle n'est pas exploitable.</summary>
    public int? Year { get; init; }

    /// <summary>Durée annoncée en secondes. La durée réelle est recalculée au traitement.</summary>
    public int DurationSeconds { get; init; }

    /// <summary>Miniature de la vidéo, ou <c>null</c> si aucune n'a pu être récupérée.</summary>
    public byte[]? Thumbnail { get; init; }

    /// <summary>Efface le dossier de travail. Un échec de suppression n'est jamais propagé.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WorkingDirectory))
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Le dossier temporaire reste sur disque : il sera repris par le nettoyage système.
        }
    }
}

/// <summary>
/// Téléchargement de la piste audio d'une vidéo en ligne.
///
/// L'abstraction isole l'application de l'outil réellement utilisé : l'implémentation
/// par défaut s'appuie sur <c>yt-dlp</c>, mais elle peut être remplacée sans toucher
/// aux cas d'utilisation.
/// </summary>
public interface IAudioDownloader
{
    /// <summary>
    /// Télécharge la piste audio et la miniature de la vidéo désignée par <paramref name="url"/>.
    ///
    /// Lève une <c>ServiceUnavailableException</c> si l'outil de téléchargement n'est pas
    /// installé, et une <c>UnprocessableException</c> si la vidéo ne peut pas être récupérée.
    /// </summary>
    /// <param name="url">URL de la vidéo, déjà validée par l'appelant.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<DownloadedAudio> DownloadAsync(string url, CancellationToken cancellationToken = default);
}
