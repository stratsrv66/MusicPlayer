namespace MusicPlatform.Application.Abstractions;

/// <summary>Playlist telle qu'exposée par la plateforme d'origine.</summary>
/// <param name="Id">Identifiant de la playlist chez la plateforme.</param>
/// <param name="Name">Nom affiché.</param>
/// <param name="Owner">Nom de la chaîne ayant publié la playlist, si connu.</param>
/// <param name="CoverUrl">URL de la vignette, si disponible.</param>
/// <param name="TrackCount">Nombre de morceaux annoncé.</param>
/// <param name="Url">URL publique de la playlist.</param>
public sealed record ExternalPlaylist(
    string Id,
    string Name,
    string? Owner,
    string? CoverUrl,
    int TrackCount,
    string Url);

/// <summary>
/// Morceau relevé lors de l'énumération d'une playlist.
///
/// Ces métadonnées restent sommaires : l'énumération ne télécharge rien. Les métadonnées
/// définitives sont celles lues par le téléchargeur, comme pour un morceau importé seul.
/// </summary>
public sealed record ExternalTrack
{
    /// <summary>Identifiant de la vidéo chez la plateforme d'origine.</summary>
    public required string SourceId { get; init; }

    public required string Title { get; init; }
    public required string ArtistName { get; init; }

    /// <summary>Durée annoncée en secondes, ou zéro si la plateforme ne la fournit pas.</summary>
    public int DurationSeconds { get; init; }

    /// <summary>URL de la vidéo, transmise telle quelle au téléchargeur.</summary>
    public string? SourceUrl { get; init; }
}

/// <summary>
/// Accès en lecture aux playlists de la plateforme d'origine.
///
/// Le provider ne fait que produire des métadonnées : il ne télécharge aucun média et ne
/// connaît ni la bibliothèque ni le stockage. L'abstraction existe pour que la couche
/// applicative ignore l'outil réellement employé.
/// </summary>
public interface IPlaylistProvider
{
    /// <summary>
    /// Extrait l'identifiant de playlist d'une URL ou d'un identifiant brut.
    /// Retourne <c>null</c> si l'entrée ne désigne pas une playlist exploitable.
    /// </summary>
    string? TryParsePlaylistId(string urlOrId);

    /// <summary>Décrit une playlist sans énumérer ses morceaux.</summary>
    Task<ExternalPlaylist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>Énumère les morceaux d'une playlist, dans leur ordre d'origine.</summary>
    Task<IReadOnlyList<ExternalTrack>> GetTracksAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>Liste les playlists publiques d'une chaîne.</summary>
    Task<IReadOnlyList<ExternalPlaylist>> ListProfilePlaylistsAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}
