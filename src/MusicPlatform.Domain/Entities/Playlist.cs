using MusicPlatform.Domain.Enums;
using MusicPlatform.Domain.Exceptions;

namespace MusicPlatform.Domain.Entities;

/// <summary>Playlist créée par un utilisateur.</summary>
public sealed class Playlist
{
    /// <summary>Nombre maximal de morceaux par playlist, pour borner les opérations de réordonnancement.</summary>
    public const int MaxItems = 500;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ContentVisibility Visibility { get; set; } = ContentVisibility.Private;
    public Guid? CoverFileId { get; set; }
    public StoredFile? CoverFile { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
    public ICollection<PlaylistFollow> Follows { get; set; } = new List<PlaylistFollow>();
    public ICollection<PlaylistFavorite> Favorites { get; set; } = new List<PlaylistFavorite>();

    /// <summary>Indique si <paramref name="viewerId"/> peut consulter cette playlist.</summary>
    public bool IsAccessibleBy(Guid? viewerId, UserRole viewerRole)
    {
        if (viewerId is not null && viewerId == OwnerId)
        {
            return true;
        }

        if (viewerRole is UserRole.Moderator or UserRole.Admin)
        {
            return true;
        }

        return Visibility != ContentVisibility.Private;
    }

    /// <summary>Indique si <paramref name="viewerId"/> peut modifier cette playlist.</summary>
    public bool IsManageableBy(Guid? viewerId, UserRole viewerRole) =>
        (viewerId is not null && viewerId == OwnerId) || viewerRole == UserRole.Admin;

    /// <summary>
    /// Applique un nouvel ordre aux éléments de la playlist. Les positions fournies doivent
    /// couvrir exactement les morceaux présents, sans doublon ni trou.
    /// </summary>
    public void Reorder(IReadOnlyDictionary<Guid, int> positionByTrackId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(positionByTrackId);

        if (positionByTrackId.Count != Items.Count)
        {
            throw new DomainException("PLAYLIST_REORDER_INVALID", "The reorder request must list every track of the playlist exactly once.");
        }

        var expectedPositions = new HashSet<int>(Enumerable.Range(0, Items.Count));
        foreach (var position in positionByTrackId.Values)
        {
            if (!expectedPositions.Remove(position))
            {
                throw new DomainException("PLAYLIST_REORDER_INVALID", "Positions must be a contiguous range starting at zero, without duplicates.");
            }
        }

        foreach (var item in Items)
        {
            if (!positionByTrackId.TryGetValue(item.TrackId, out var position))
            {
                throw new DomainException("PLAYLIST_REORDER_INVALID", "The reorder request does not cover every track of the playlist.");
            }

            item.Position = position;
        }

        UpdatedAt = now;
    }
}

/// <summary>Morceau positionné dans une playlist.</summary>
public sealed class PlaylistItem
{
    public Guid PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;

    /// <summary>Rang du morceau dans la playlist, à partir de zéro.</summary>
    public int Position { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Abonnement d'un utilisateur à une playlist.</summary>
public sealed class PlaylistFollow
{
    public Guid PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Mise en favori d'une playlist par un utilisateur.</summary>
public sealed class PlaylistFavorite
{
    public Guid PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
