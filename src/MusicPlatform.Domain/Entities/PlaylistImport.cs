using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Domain.Entities;

/// <summary>
/// Import d'une playlist YouTube dans la bibliothèque d'un utilisateur.
///
/// L'inventaire des morceaux est figé en base dès la création : le traitement peut
/// donc être interrompu puis repris sans réinterroger YouTube, et la progression reste
/// consultable après un redémarrage du serveur.
/// </summary>
public sealed class PlaylistImport
{
    /// <summary>Nombre maximal de morceaux acceptés dans un même import.</summary>
    public const int MaxTracks = 500;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Plateforme d'origine de la playlist.</summary>
    public ExternalPlatform Platform { get; set; } = ExternalPlatform.Youtube;

    /// <summary>Identifiant de la playlist chez la plateforme d'origine.</summary>
    public string SourcePlaylistId { get; set; } = string.Empty;

    /// <summary>URL publique de la playlist, conservée pour l'affichage.</summary>
    public string? SourceUrl { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre de morceaux inventoriés à la création de l'import.</summary>
    public int TotalTracks { get; set; }

    public PlaylistImportStatus Status { get; set; } = PlaylistImportStatus.Pending;

    /// <summary>Visibilité appliquée aux morceaux créés par cet import.</summary>
    public ContentVisibility Visibility { get; set; } = ContentVisibility.Private;

    /// <summary>Playlist créée dans la bibliothèque pour refléter la playlist importée.</summary>
    public Guid? PlaylistId { get; set; }
    public Playlist? Playlist { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<PlaylistImportItem> Items { get; set; } = new List<PlaylistImportItem>();

    /// <summary>Vrai lorsque l'import n'évoluera plus sans action de l'utilisateur.</summary>
    public bool IsTerminal => Status is PlaylistImportStatus.Completed
        or PlaylistImportStatus.Failed
        or PlaylistImportStatus.Cancelled;
}

/// <summary>
/// Morceau inventorié dans un import de playlist, et l'état de son traitement.
///
/// Les métadonnées relevées ici proviennent de l'énumération de la playlist, qui reste
/// sommaire. Les métadonnées définitives du morceau sont celles lues par yt-dlp au
/// téléchargement, exactement comme pour l'import d'un morceau seul.
/// </summary>
public sealed class PlaylistImportItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImportId { get; set; }
    public PlaylistImport Import { get; set; } = null!;

    /// <summary>Rang du morceau dans la playlist d'origine, à partir de zéro.</summary>
    public int Position { get; set; }

    /// <summary>Identifiant de la vidéo chez la plateforme d'origine.</summary>
    public string SourceTrackId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;

    /// <summary>Durée annoncée par la plateforme, en secondes.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>URL de la vidéo, transmise telle quelle au téléchargeur.</summary>
    public string? SourceUrl { get; set; }

    public PlaylistImportItemStatus Status { get; set; } = PlaylistImportItemStatus.Pending;
    public string? FailureReason { get; set; }

    /// <summary>Nombre de tentatives de traitement, relances comprises.</summary>
    public int Attempts { get; set; }

    /// <summary>Morceau de la bibliothèque créé ou rattaché pour cette entrée.</summary>
    public Guid? TrackId { get; set; }
    public Track? Track { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Vrai lorsque l'entrée n'évoluera plus sans relance explicite.</summary>
    public bool IsTerminal => Status is PlaylistImportItemStatus.Imported
        or PlaylistImportItemStatus.Duplicate
        or PlaylistImportItemStatus.Failed
        or PlaylistImportItemStatus.Cancelled;
}
