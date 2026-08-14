using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Contracts;

/// <summary>Chiffres clés du tableau de bord artiste.</summary>
public sealed record AnalyticsOverviewDto
{
    public required int TrackCount { get; init; }
    public required int PublicTrackCount { get; init; }
    public required long TotalPlays { get; init; }
    public required long TotalLikes { get; init; }
    public required int FollowerCount { get; init; }
    public required int CommentCount { get; init; }

    /// <summary>Écoutes des 30 derniers jours, pour situer la tendance récente.</summary>
    public required long PlaysLast30Days { get; init; }
}

/// <summary>Statistiques détaillées d'un morceau du propriétaire.</summary>
public sealed record TrackAnalyticsDto
{
    public required Guid TrackId { get; init; }
    public required string Title { get; init; }
    public required long PlayCount { get; init; }
    public required long LikeCount { get; init; }
    public required int CommentCount { get; init; }

    /// <summary>Nombre de playlists contenant ce morceau.</summary>
    public required int PlaylistCount { get; init; }

    public required ContentVisibility Visibility { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>Point d'une série temporelle d'écoutes.</summary>
public sealed record PlaysPointDto(DateOnly Date, long Plays, int UniqueListeners);

/// <summary>Granularité d'agrégation des séries temporelles.</summary>
public enum AnalyticsGroupBy
{
    Day = 0,
    Week = 1,
    Month = 2,
}

/// <summary>Série temporelle d'écoutes sur une période.</summary>
public sealed record PlaysSeriesDto(DateTime From, DateTime To, AnalyticsGroupBy GroupBy, IReadOnlyList<PlaysPointDto> Points);

/// <summary>Signalement tel qu'exposé à son auteur ou à la modération.</summary>
public sealed record ReportDto
{
    public required Guid Id { get; init; }
    public required ReportTargetType TargetType { get; init; }
    public required Guid TargetId { get; init; }
    public required ReportReason Reason { get; init; }
    public string? Description { get; init; }
    public required ReportStatus Status { get; init; }
    public string? ResolutionNote { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }

    /// <summary>Auteur du signalement. Nul pour la vue de l'auteur lui-même.</summary>
    public UserRefDto? Reporter { get; init; }

    /// <summary>Libellé lisible de la cible, par exemple le titre du morceau signalé.</summary>
    public string? TargetLabel { get; init; }
}

/// <summary>Création d'un signalement.</summary>
public sealed record CreateReportRequest
{
    public ReportTargetType TargetType { get; init; }
    public Guid TargetId { get; init; }
    public ReportReason Reason { get; init; }
    public string? Description { get; init; }
}

/// <summary>Traitement d'un signalement par la modération.</summary>
public sealed record ResolveReportRequest
{
    public ReportStatus Status { get; init; }
    public string? ResolutionNote { get; init; }

    /// <summary>Masque également le contenu ciblé lorsque vrai.</summary>
    public bool HideTarget { get; init; }
}

/// <summary>Filtres de la liste des signalements côté modération.</summary>
public sealed record ReportFilter
{
    public ReportStatus? Status { get; init; }
    public ReportReason? Reason { get; init; }
    public ReportTargetType? TargetType { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

/// <summary>Vue administrateur d'un utilisateur.</summary>
public sealed record AdminUserDto
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required UserRole Role { get; init; }
    public required UserStatus Status { get; init; }
    public required ProfileVisibility ProfileVisibility { get; init; }
    public required int TrackCount { get; init; }
    public required int PlaylistCount { get; init; }
    public required int FollowerCount { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
}

/// <summary>Modification administrative d'un compte.</summary>
public sealed record UpdateAdminUserRequest(UserRole? Role, UserStatus? Status);

/// <summary>Vue administrateur d'un morceau, y compris les contenus masqués.</summary>
public sealed record AdminTrackDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string ArtistName { get; init; }
    public required UserRefDto Owner { get; init; }
    public required ContentVisibility Visibility { get; init; }
    public required TrackStatus Status { get; init; }
    public required long PlayCount { get; init; }
    public required long LikeCount { get; init; }
    public required bool IsHidden { get; init; }
    public required bool IsDeleted { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>Entrée du journal d'audit.</summary>
public sealed record AuditLogDto
{
    public required Guid Id { get; init; }
    public UserRefDto? Actor { get; init; }
    public required string Action { get; init; }
    public string? TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public string? Metadata { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>Statistiques globales de la plateforme.</summary>
public sealed record AdminStatisticsDto
{
    public required int TotalUsers { get; init; }
    public required int ActiveUsers { get; init; }
    public required int SuspendedUsers { get; init; }
    public required int TotalTracks { get; init; }
    public required int PublicTracks { get; init; }
    public required int HiddenTracks { get; init; }
    public required int TotalPlaylists { get; init; }
    public required int TotalComments { get; init; }
    public required long TotalPlays { get; init; }
    public required long TotalLikes { get; init; }
    public required int PendingReports { get; init; }
    public required long StorageBytesUsed { get; init; }
    public required IReadOnlyList<PlaysPointDto> PlaysLast30Days { get; init; }
}

/// <summary>Création ou mise à jour d'un genre.</summary>
public sealed record SaveGenreRequest(string Name);
