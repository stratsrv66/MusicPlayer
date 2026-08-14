namespace MusicPlatform.Application.Abstractions;

/// <summary>Résultat de l'écriture d'un fichier dans le stockage.</summary>
/// <param name="RelativePath">Chemin logique, seul identifiant manipulé par l'application.</param>
/// <param name="SizeBytes">Taille réellement écrite.</param>
/// <param name="Sha256">Empreinte hexadécimale minuscule du contenu.</param>
public readonly record struct FileWriteResult(string RelativePath, long SizeBytes, string Sha256);

/// <summary>Informations sur un fichier présent dans le stockage.</summary>
/// <param name="SizeBytes">Taille du fichier.</param>
/// <param name="LastModifiedUtc">Date de dernière modification.</param>
public readonly record struct FileStat(long SizeBytes, DateTime LastModifiedUtc);

/// <summary>
/// Abstraction du stockage de fichiers. Les chemins manipulés sont logiques et relatifs :
/// aucune couche métier ne connaît l'emplacement physique réel, ce qui permet de remplacer
/// l'implémentation locale par un stockage objet sans toucher au domaine.
/// </summary>
public interface IFileStorage
{
    /// <summary>Écrit un flux et retourne la taille et l'empreinte du contenu persisté.</summary>
    Task<FileWriteResult> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ouvre un flux de lecture positionnable, indispensable au support des requêtes HTTP Range
    /// sans charger le fichier en mémoire. Lève si le fichier est absent.
    /// </summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ouvre un flux d'écriture, en créant ou en écrasant la cible. Permet d'écrire
    /// progressivement un gros contenu (archive d'export) sans le construire en mémoire.
    /// </summary>
    Task<Stream> OpenWriteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Retourne les informations du fichier, ou <c>null</c> s'il n'existe pas.</summary>
    Task<FileStat?> StatAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Supprime un fichier. Retourne <c>false</c> si le fichier était déjà absent.</summary>
    Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Supprime récursivement un préfixe de chemin, par exemple le dossier d'un utilisateur.</summary>
    Task DeleteDirectoryAsync(string relativePrefix, CancellationToken cancellationToken = default);

    /// <summary>Vérifie que le stockage est accessible en lecture et en écriture (health check).</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>Conventions de nommage des chemins de stockage, centralisées pour éviter les chaînes magiques.</summary>
public static class StoragePaths
{
    public const string AudioRoot = "audio";
    public const string CoversRoot = "covers";
    public const string AvatarsRoot = "avatars";
    public const string PlaylistCoversRoot = "playlist-covers";
    public const string ExportsRoot = "exports";
    public const string TempRoot = "tmp";

    /// <summary>Fichier audio de diffusion d'un morceau : <c>audio/{userId}/{trackId}{ext}</c>.</summary>
    public static string Audio(Guid ownerId, Guid trackId, string extension) =>
        $"{AudioRoot}/{ownerId}/{trackId}{extension}";

    /// <summary>Dossier contenant tous les fichiers audio d'un utilisateur.</summary>
    public static string AudioDirectory(Guid ownerId) => $"{AudioRoot}/{ownerId}";

    /// <summary>Pochette d'un morceau dans une taille donnée : <c>covers/{size}/{trackId}.webp</c>.</summary>
    public static string Cover(string sizeSlug, Guid trackId) => $"{CoversRoot}/{sizeSlug}/{trackId}.webp";

    /// <summary>Avatar d'un utilisateur.</summary>
    public static string Avatar(Guid fileId) => $"{AvatarsRoot}/{fileId}.webp";

    /// <summary>Pochette d'une playlist.</summary>
    public static string PlaylistCover(Guid fileId) => $"{PlaylistCoversRoot}/{fileId}.webp";

    /// <summary>Archive d'export des données d'un utilisateur.</summary>
    public static string Export(Guid userId, Guid exportId) => $"{ExportsRoot}/{userId}/{exportId}.zip";

    /// <summary>Dossier des exports d'un utilisateur.</summary>
    public static string ExportDirectory(Guid userId) => $"{ExportsRoot}/{userId}";

    /// <summary>Fichier temporaire lié à une opération d'upload.</summary>
    public static string Temp(Guid operationId, string extension) => $"{TempRoot}/{operationId}{extension}";
}
