using MusicPlatform.Domain.Enums;
using MusicPlatform.Domain.Exceptions;

namespace MusicPlatform.Domain.Entities;

/// <summary>Compte utilisateur de la plateforme.</summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>Version minuscule de <see cref="Username"/>, utilisée pour l'unicité et la recherche.</summary>
    public string UsernameNormalized { get; set; } = string.Empty;

    public string? Bio { get; set; }
    public Guid? AvatarFileId { get; set; }
    public StoredFile? AvatarFile { get; set; }

    /// <summary>Liens sociaux sérialisés en JSON sous forme d'objet { "label": "url" }.</summary>
    public string? SocialLinks { get; set; }

    public ProfileVisibility ProfileVisibility { get; set; } = ProfileVisibility.Public;
    public UserRole Role { get; set; } = UserRole.User;
    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Date d'anonymisation logique du compte. Un compte supprimé ne peut plus se connecter.</summary>
    public DateTime? DeletedAt { get; set; }

    public UserSettings Settings { get; set; } = null!;
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
    public ICollection<Playlist> Playlists { get; set; } = new List<Playlist>();

    /// <summary>Abonnements dont cet utilisateur est la cible.</summary>
    public ICollection<Follow> Followers { get; set; } = new List<Follow>();

    /// <summary>Abonnements souscrits par cet utilisateur.</summary>
    public ICollection<Follow> Following { get; set; } = new List<Follow>();

    /// <summary>Vrai si le compte peut s'authentifier et agir sur la plateforme.</summary>
    public bool IsActive => DeletedAt is null && Status == UserStatus.Active;

    /// <summary>Vrai si l'utilisateur dispose des droits de modération.</summary>
    public bool CanModerate => Role is UserRole.Moderator or UserRole.Admin;

    /// <summary>Vrai si l'utilisateur dispose des droits d'administration.</summary>
    public bool IsAdmin => Role == UserRole.Admin;

    /// <summary>
    /// Indique si <paramref name="viewerId"/> a le droit de consulter le détail de ce profil.
    /// Un profil privé n'est visible que par son propriétaire et par la modération.
    /// </summary>
    public bool IsProfileVisibleTo(Guid? viewerId, UserRole viewerRole)
    {
        if (viewerId == Id)
        {
            return true;
        }

        if (viewerRole is UserRole.Moderator or UserRole.Admin)
        {
            return true;
        }

        return DeletedAt is null && ProfileVisibility == ProfileVisibility.Public;
    }
}

/// <summary>Préférences d'affichage propres à un utilisateur.</summary>
public sealed class UserSettings
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Affiche publiquement le nombre de likes des morceaux de l'utilisateur.</summary>
    public bool ShowLikeCount { get; set; } = true;

    /// <summary>Affiche publiquement le nombre d'écoutes des morceaux de l'utilisateur.</summary>
    public bool ShowPlayCount { get; set; } = true;
}

/// <summary>
/// Refresh token persisté afin de permettre la rotation et la révocation explicite
/// (logout, suppression de compte). Seul le hash du token est stocké.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Hash SHA-256 du token remis au client. Le token en clair n'est jamais stocké.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    /// <summary>Token émis en remplacement lors de la rotation, pour tracer une réutilisation.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>Vrai si le token est encore utilisable à l'instant <paramref name="now"/>.</summary>
    public bool IsUsable(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>Relation d'abonnement entre deux utilisateurs.</summary>
public sealed class Follow
{
    public Guid FollowerId { get; set; }
    public User Follower { get; set; } = null!;
    public Guid FollowedId { get; set; }
    public User Followed { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Crée un abonnement en refusant l'auto-abonnement.</summary>
    public static Follow Create(Guid followerId, Guid followedId)
    {
        if (followerId == followedId)
        {
            throw new DomainException("FOLLOW_SELF_NOT_ALLOWED", "A user cannot follow themselves.");
        }

        return new Follow { FollowerId = followerId, FollowedId = followedId };
    }
}

/// <summary>Demande d'export des données personnelles d'un utilisateur.</summary>
public sealed class UserExport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public UserExportStatus Status { get; set; } = UserExportStatus.Pending;
    public string? StoragePath { get; set; }
    public long? FileSize { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Vrai si l'archive est disponible au téléchargement à l'instant <paramref name="now"/>.</summary>
    public bool IsDownloadable(DateTime now) =>
        Status == UserExportStatus.Ready
        && StoragePath is not null
        && (ExpiresAt is null || ExpiresAt > now);
}
