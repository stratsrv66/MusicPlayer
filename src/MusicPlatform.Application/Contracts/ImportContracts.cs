using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Contracts;

/// <summary>Playlist YouTube relevée avant import.</summary>
public sealed record ExternalPlaylistDto(
    string Id,
    string Name,
    string? Owner,
    string? CoverUrl,
    int TrackCount,
    string Url);

/// <summary>
/// Morceau relevé lors de l'énumération d'une playlist, tel que présenté dans l'aperçu.
/// Les métadonnées définitives sont celles lues au téléchargement.
/// </summary>
public sealed record ExternalTrackDto(
    string SourceId,
    string Title,
    string ArtistName,
    int DurationSeconds);

/// <summary>Aperçu d'une playlist avant import : de quoi décider en connaissance de cause.</summary>
public sealed record PlaylistPreviewDto(ExternalPlaylistDto Playlist, IReadOnlyList<ExternalTrackDto> Tracks);

/// <summary>Demande d'aperçu d'une playlist YouTube.</summary>
public sealed record PreviewPlaylistRequest
{
    /// <summary>URL de la playlist, ou son identifiant YouTube.</summary>
    public string? Url { get; init; }
}

/// <summary>Demande d'import d'une playlist YouTube.</summary>
public sealed record StartPlaylistImportRequest
{
    /// <summary>URL de la playlist, ou son identifiant YouTube.</summary>
    public string? Url { get; init; }

    /// <summary>Visibilité appliquée aux morceaux créés. Privée par défaut.</summary>
    public ContentVisibility Visibility { get; init; } = ContentVisibility.Private;

    /// <summary>Crée dans la bibliothèque une playlist reflétant celle importée.</summary>
    public bool CreatePlaylist { get; init; } = true;
}

/// <summary>État d'un morceau au sein d'un import.</summary>
public sealed record PlaylistImportItemDto(
    Guid Id,
    int Position,
    string Title,
    string ArtistName,
    int DurationSeconds,
    PlaylistImportItemStatus Status,
    string? FailureReason,
    int Attempts,
    Guid? TrackId);

/// <summary>Décompte des morceaux par état, pour l'affichage de la progression.</summary>
/// <param name="Total">Nombre total de morceaux inventoriés.</param>
/// <param name="Processed">Morceaux ayant atteint un état terminal.</param>
public sealed record PlaylistImportProgressDto(
    int Total,
    int Processed,
    int Pending,
    int Running,
    int Imported,
    int Duplicate,
    int Failed,
    int Cancelled);

/// <summary>Import de playlist et sa progression.</summary>
public sealed record PlaylistImportDto(
    Guid Id,
    string Name,
    string? SourceUrl,
    PlaylistImportStatus Status,
    ContentVisibility Visibility,
    Guid? PlaylistId,
    string? FailureReason,
    PlaylistImportProgressDto Progress,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>Import de playlist accompagné du détail de ses morceaux.</summary>
public sealed record PlaylistImportDetailsDto(PlaylistImportDto Import, IReadOnlyList<PlaylistImportItemDto> Items);
