using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Contracts;

/// <summary>Construit les URL de médias exposées au client, sans jamais révéler de chemin physique.</summary>
public static class MediaUrls
{
    public const string Base = "/api/v1";

    public static string TrackCover(Guid trackId, string size) => $"{Base}/tracks/{trackId}/cover/{size}";

    public static string TrackStream(Guid trackId) => $"{Base}/tracks/{trackId}/stream";

    public static string? Avatar(Guid? fileId) => fileId is null ? null : $"{Base}/media/avatars/{fileId}";

    public static string? PlaylistCover(Guid? fileId) => fileId is null ? null : $"{Base}/media/playlist-covers/{fileId}";
}

/// <summary>Jetons retournés après une authentification réussie.</summary>
public sealed record AuthResponseDto(string AccessToken, string RefreshToken, int ExpiresIn, UserProfileDto User);

/// <summary>Demande de création de compte.</summary>
public sealed record RegisterRequest(string Email, string Username, string Password);

/// <summary>Demande de connexion.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Demande de renouvellement ou de révocation d'un refresh token.</summary>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>Les trois tailles de pochette exposées pour un morceau.</summary>
public sealed record CoverUrlsDto(string Small, string Medium, string Large);

/// <summary>Profil public d'un utilisateur.</summary>
public sealed record UserProfileDto
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public string? Bio { get; init; }
    public string? AvatarUrl { get; init; }
    public IReadOnlyDictionary<string, string>? SocialLinks { get; init; }
    public required ProfileVisibility ProfileVisibility { get; init; }
    public required UserRole Role { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required int TrackCount { get; init; }
    public required int PlaylistCount { get; init; }
    public required int FollowerCount { get; init; }
    public required int FollowingCount { get; init; }

    /// <summary>Vrai si l'utilisateur courant suit ce profil. Nul pour un appel anonyme.</summary>
    public bool? IsFollowedByCurrentUser { get; init; }

    /// <summary>Vrai si le profil est privé et que le détail n'est donc pas exposé.</summary>
    public required bool IsRestricted { get; init; }
}

/// <summary>Profil de l'utilisateur connecté, avec les informations privées.</summary>
public sealed record MeDto
{
    public required UserProfileDto Profile { get; init; }
    public required string Email { get; init; }
    public required UserSettingsDto Settings { get; init; }
    public required UserStatus Status { get; init; }
}

/// <summary>
/// Référence minimale vers un utilisateur, imbriquée dans les autres DTO.
/// Volontairement sans compteurs : les calculer pour chaque élément d'une liste
/// provoquerait des sous-requêtes corrélées inutiles.
/// </summary>
public sealed record UserRefDto(Guid Id, string Username, string? AvatarUrl);

/// <summary>Résumé d'utilisateur utilisé dans les listes d'utilisateurs.</summary>
public sealed record UserSummaryDto(Guid Id, string Username, string? AvatarUrl, int FollowerCount, int TrackCount);

/// <summary>Préférences d'affichage de l'utilisateur.</summary>
public sealed record UserSettingsDto(bool ShowLikeCount, bool ShowPlayCount);

/// <summary>Mise à jour partielle du profil. Les champs nuls ne sont pas modifiés.</summary>
public sealed record UpdateProfileRequest
{
    public string? Username { get; init; }
    public string? Bio { get; init; }
    public IReadOnlyDictionary<string, string>? SocialLinks { get; init; }
    public ProfileVisibility? ProfileVisibility { get; init; }
}

/// <summary>Mise à jour partielle des préférences.</summary>
public sealed record UpdateSettingsRequest(bool? ShowLikeCount, bool? ShowPlayCount);

/// <summary>Demande de suppression du compte, exigeant une confirmation explicite.</summary>
public sealed record DeleteAccountRequest
{
    /// <summary>Doit valoir exactement le nom d'utilisateur du compte pour confirmer l'intention.</summary>
    public string? ConfirmUsername { get; init; }

    /// <summary>Doit valoir <c>true</c> : garde-fou supplémentaire contre les appels accidentels.</summary>
    public bool Confirm { get; init; }
}

/// <summary>État d'une demande d'export de données.</summary>
public sealed record UserExportDto
{
    public required Guid Id { get; init; }
    public required UserExportStatus Status { get; init; }
    public long? FileSize { get; init; }
    public string? FailureReason { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    /// <summary>URL de téléchargement, présente uniquement lorsque l'archive est disponible.</summary>
    public string? DownloadUrl { get; init; }
}

/// <summary>Entrée d'historique d'écoute.</summary>
public sealed record HistoryEntryDto(TrackDto Track, int LastPositionSeconds, DateTime LastPlayedAt);

/// <summary>Dernière position d'écoute connue sur un morceau.</summary>
public sealed record PlaybackProgressDto(Guid TrackId, int PositionSeconds, DateTime? LastPlayedAt);
