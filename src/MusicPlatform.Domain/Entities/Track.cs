using MusicPlatform.Domain.Enums;
using MusicPlatform.Domain.Exceptions;

namespace MusicPlatform.Domain.Entities;

/// <summary>Morceau publié par un utilisateur.</summary>
public sealed class Track
{
    /// <summary>Taille maximale acceptée pour un fichier audio, en octets (20 Mo).</summary>
    public const long MaxAudioFileSizeBytes = 20L * 1024 * 1024;

    /// <summary>Durée d'écoute minimale, en secondes, pour qu'une lecture soit comptabilisée.</summary>
    public const int MinimumValidPlaySeconds = 10;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public Guid? AlbumId { get; set; }
    public Album? Album { get; set; }
    public Guid? GenreId { get; set; }
    public Genre? Genre { get; set; }

    public string Title { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public ContentVisibility Visibility { get; set; } = ContentVisibility.Private;
    public TrackStatus Status { get; set; } = TrackStatus.Uploading;
    public string? Description { get; set; }
    public int? Year { get; set; }

    /// <summary>Compteur dénormalisé maintenu par les cas d'utilisation like/unlike.</summary>
    public long LikeCount { get; set; }

    /// <summary>Compteur dénormalisé maintenu à partir des <see cref="PlayEvent"/> valides.</summary>
    public long PlayCount { get; set; }

    /// <summary>Raison technique du dernier échec de traitement, affichée au propriétaire.</summary>
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    /// <summary>Date de masquage par la modération. Distincte d'une suppression.</summary>
    public DateTime? HiddenAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public TrackFile? File { get; set; }
    public TrackMetadata? Metadata { get; set; }
    public ICollection<TrackCover> Covers { get; set; } = new List<TrackCover>();
    public ICollection<TrackTag> TrackTags { get; set; } = new List<TrackTag>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<TrackLike> Likes { get; set; } = new List<TrackLike>();
    public ICollection<PlaylistItem> PlaylistItems { get; set; } = new List<PlaylistItem>();

    /// <summary>Vrai si le morceau est écoutable : traité, non supprimé, non masqué.</summary>
    public bool IsPlayable => Status == TrackStatus.Ready && DeletedAt is null && HiddenAt is null;

    /// <summary>Vrai si le morceau doit apparaître dans les listes et recherches publiques.</summary>
    public bool IsPubliclyListed => IsPlayable && Visibility == ContentVisibility.Public;

    /// <summary>
    /// Indique si <paramref name="viewerId"/> peut consulter et écouter ce morceau.
    /// Le propriétaire et la modération y accèdent toujours ; les autres uniquement si le
    /// morceau est écoutable et non privé (un morceau <c>UNLISTED</c> reste accessible par lien).
    /// </summary>
    public bool IsAccessibleBy(Guid? viewerId, UserRole viewerRole)
    {
        if (viewerId is not null && viewerId == OwnerId)
        {
            return DeletedAt is null;
        }

        if (viewerRole is UserRole.Moderator or UserRole.Admin)
        {
            return DeletedAt is null;
        }

        return IsPlayable && Visibility != ContentVisibility.Private;
    }

    /// <summary>Indique si <paramref name="viewerId"/> peut modifier ou supprimer ce morceau.</summary>
    public bool IsManageableBy(Guid? viewerId, UserRole viewerRole) =>
        DeletedAt is null && ((viewerId is not null && viewerId == OwnerId) || viewerRole == UserRole.Admin);

    /// <summary>
    /// Rend le morceau public. Refuse la publication tant que le pipeline de traitement
    /// n'a pas abouti, afin de ne jamais exposer un morceau sans fichier utilisable.
    /// </summary>
    public void Publish(DateTime now)
    {
        if (Status != TrackStatus.Ready)
        {
            throw new DomainException("TRACK_NOT_READY", "The track cannot be published before processing completes.");
        }

        Visibility = ContentVisibility.Public;
        PublishedAt ??= now;
        UpdatedAt = now;
    }

    /// <summary>Retire le morceau de la publication en le repassant en privé.</summary>
    public void Unpublish(DateTime now)
    {
        Visibility = ContentVisibility.Private;
        UpdatedAt = now;
    }

    /// <summary>Marque le morceau comme prêt à l'écoute à l'issue du traitement.</summary>
    public void MarkReady(int durationSeconds, DateTime now)
    {
        if (durationSeconds <= 0)
        {
            throw new DomainException("TRACK_UPLOAD_INVALID", "The processed track has an invalid duration.");
        }

        DurationSeconds = durationSeconds;
        Status = TrackStatus.Ready;
        FailureReason = null;
        UpdatedAt = now;

        if (Visibility == ContentVisibility.Public)
        {
            PublishedAt ??= now;
        }
    }

    /// <summary>Marque le traitement comme échoué en conservant la raison pour le propriétaire.</summary>
    public void MarkFailed(string reason, DateTime now)
    {
        Status = TrackStatus.Failed;
        FailureReason = reason;
        UpdatedAt = now;
    }
}

/// <summary>Fichier audio de diffusion associé à un morceau.</summary>
public sealed class TrackFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public string StoragePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }

    /// <summary>Empreinte SHA-256 du fichier, utilisée pour la détection de doublons et l'ETag.</summary>
    public string Checksum { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Métadonnées extraites du fichier d'origine, conservées telles quelles.</summary>
public sealed class TrackMetadata
{
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public string? OriginalFilename { get; set; }
    public string? EmbeddedTitle { get; set; }
    public string? EmbeddedArtist { get; set; }
    public string? EmbeddedAlbum { get; set; }
    public string? EmbeddedGenre { get; set; }
    public int? EmbeddedYear { get; set; }

    /// <summary>Métadonnées complémentaires sérialisées en JSON (bitrate, codec, etc.).</summary>
    public string? MetadataJson { get; set; }
}

/// <summary>Déclinaison d'une pochette dans une taille donnée.</summary>
public sealed class TrackCover
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public CoverSize Size { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/webp";
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Like posé par un utilisateur sur un morceau.</summary>
public sealed class TrackLike
{
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Opération d'upload d'un fichier audio, utilisée pour tracer et nettoyer les échecs.</summary>
public sealed class UploadOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TrackId { get; set; }
    public Track? Track { get; set; }
    public UploadOperationStatus Status { get; set; } = UploadOperationStatus.Uploading;
    public string OriginalFilename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }

    /// <summary>Chemin du fichier temporaire, supprimé dès que l'opération se termine.</summary>
    public string? TemporaryPath { get; set; }

    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
