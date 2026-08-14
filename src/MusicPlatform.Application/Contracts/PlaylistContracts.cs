using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Contracts;

/// <summary>Représentation d'une playlist dans les listes.</summary>
public sealed record PlaylistDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required ContentVisibility Visibility { get; init; }
    public string? CoverUrl { get; init; }
    public required UserRefDto Owner { get; init; }
    public required int TrackCount { get; init; }
    public required int TotalDurationSeconds { get; init; }
    public required int FollowerCount { get; init; }
    public bool? IsFollowedByCurrentUser { get; init; }
    public bool? IsFavoritedByCurrentUser { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>Playlist accompagnée de ses premiers morceaux.</summary>
public sealed record PlaylistDetailsDto(PlaylistDto Playlist, IReadOnlyList<PlaylistTrackDto> Tracks, bool CanEdit);

/// <summary>Morceau positionné dans une playlist.</summary>
public sealed record PlaylistTrackDto(TrackDto Track, int Position, DateTime AddedAt);

/// <summary>Création d'une playlist.</summary>
public sealed record CreatePlaylistRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public ContentVisibility Visibility { get; init; } = ContentVisibility.Private;
}

/// <summary>Mise à jour partielle d'une playlist.</summary>
public sealed record UpdatePlaylistRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public ContentVisibility? Visibility { get; init; }

    /// <summary>Retire la pochette personnalisée lorsque vrai.</summary>
    public bool ClearCover { get; init; }
}

/// <summary>Ajout d'un morceau à une playlist.</summary>
public sealed record AddPlaylistTrackRequest(Guid TrackId);

/// <summary>Nouvelle position d'un morceau dans une playlist.</summary>
public sealed record ReorderItem(Guid TrackId, int Position);

/// <summary>Réordonnancement complet d'une playlist.</summary>
public sealed record ReorderPlaylistRequest(IReadOnlyList<ReorderItem> Items);

/// <summary>Duplication d'une playlist existante.</summary>
public sealed record DuplicatePlaylistRequest(string? Name, ContentVisibility? Visibility);

/// <summary>Types de contenus interrogeables par la recherche.</summary>
public enum SearchType
{
    All = 0,
    Track = 1,
    User = 2,
    Album = 3,
    Playlist = 4,
    Tag = 5,
}

/// <summary>Paramètres de recherche.</summary>
public sealed record SearchQuery
{
    public string? Q { get; init; }
    public SearchType Type { get; init; } = SearchType.All;
    public string? Genre { get; init; }
    public string? Tag { get; init; }
    public string? Artist { get; init; }
    public int? MinDuration { get; init; }
    public int? MaxDuration { get; init; }
    public string? Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Résultats agrégés d'une recherche.</summary>
public sealed record SearchResultDto
{
    public required SearchType Type { get; init; }
    public required string? Query { get; init; }
    public PagedResultDto<TrackDto>? Tracks { get; init; }
    public PagedResultDto<UserSummaryDto>? Users { get; init; }
    public PagedResultDto<AlbumDto>? Albums { get; init; }
    public PagedResultDto<PlaylistDto>? Playlists { get; init; }
    public PagedResultDto<TagDto>? Tags { get; init; }
}

/// <summary>Forme sérialisée d'une page de résultats.</summary>
public sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalItems, int TotalPages);

/// <summary>Contenu de la page d'accueil.</summary>
public sealed record HomeDto
{
    public required IReadOnlyList<TrackDto> RecentTracks { get; init; }
    public required IReadOnlyList<TrackDto> PopularTracks { get; init; }
    public required IReadOnlyList<UserSummaryDto> PopularArtists { get; init; }
    public required IReadOnlyList<PlaylistDto> PopularPlaylists { get; init; }
    public required IReadOnlyList<TrackDto> Recommendations { get; init; }
    public required IReadOnlyList<TrackDto> FromFollowedArtists { get; init; }
}
