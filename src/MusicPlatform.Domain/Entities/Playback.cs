using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Domain.Entities;

/// <summary>Commentaire posté sur un morceau, éventuellement positionné dans le temps.</summary>
public sealed class Comment
{
    /// <summary>Longueur maximale du texte d'un commentaire.</summary>
    public const int MaxContentLength = 2000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public string Content { get; set; } = string.Empty;

    /// <summary>Position dans le morceau à laquelle le commentaire se rattache, en secondes.</summary>
    public int? TimestampSeconds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    /// <summary>Seul l'auteur peut modifier son commentaire.</summary>
    public bool IsEditableBy(Guid viewerId) => DeletedAt is null && AuthorId == viewerId;

    /// <summary>
    /// L'auteur, le propriétaire du morceau et la modération peuvent supprimer un commentaire.
    /// </summary>
    public bool IsDeletableBy(Guid viewerId, UserRole viewerRole, Guid trackOwnerId) =>
        DeletedAt is null
        && (AuthorId == viewerId || trackOwnerId == viewerId || viewerRole is UserRole.Moderator or UserRole.Admin);
}

/// <summary>Écoute validée d'un morceau, source détaillée des statistiques.</summary>
public sealed class PlayEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;

    /// <summary>Nul pour une écoute anonyme.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Identifiant de session client, utilisé pour la déduplication des écoutes anonymes.</summary>
    public Guid? SessionId { get; set; }

    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public int DurationSeconds { get; set; }

    /// <summary>Origine de la lecture, par exemple <c>PLAYER</c>.</summary>
    public string? Source { get; set; }
}

/// <summary>Dernière position d'écoute connue d'un utilisateur sur un morceau.</summary>
public sealed class ListeningHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public int LastPositionSeconds { get; set; }
    public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
}
