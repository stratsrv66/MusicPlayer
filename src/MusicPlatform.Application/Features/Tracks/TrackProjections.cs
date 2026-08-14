using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>
/// Forme intermédiaire d'un morceau, remplie par une projection SQL unique.
/// Elle évite de charger les entités EF et supprime les requêtes N+1 sur le propriétaire,
/// le genre, les tags et l'état du like.
/// </summary>
public sealed class TrackProjection
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ArtistName { get; init; } = string.Empty;
    public int DurationSeconds { get; init; }
    public ContentVisibility Visibility { get; init; }
    public TrackStatus Status { get; init; }
    public long LikeCount { get; init; }
    public long PlayCount { get; init; }

    public Guid OwnerId { get; init; }
    public string OwnerUsername { get; init; } = string.Empty;
    public Guid? OwnerAvatarFileId { get; init; }
    public bool OwnerShowLikeCount { get; init; }
    public bool OwnerShowPlayCount { get; init; }

    public Guid? GenreId { get; init; }
    public string? GenreName { get; init; }
    public string? GenreSlug { get; init; }

    public Guid? AlbumId { get; init; }
    public string? AlbumName { get; init; }
    public string? AlbumArtistName { get; init; }

    public List<string> Tags { get; init; } = [];

    public string? Description { get; init; }
    public int? Year { get; init; }
    public string? FailureReason { get; init; }
    public DateTime? HiddenAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }

    /// <summary>Vrai si l'utilisateur courant a liké ce morceau, faux pour un appel anonyme.</summary>
    public bool IsLiked { get; init; }
}

/// <summary>Filtrage de visibilité et projections réutilisées par toutes les listes de morceaux.</summary>
public static class TrackQueries
{
    /// <summary>Morceaux publiquement référencés : prêts, publics, ni masqués ni supprimés.</summary>
    public static IQueryable<Track> PubliclyListed(this IQueryable<Track> query) =>
        query.Where(t => t.DeletedAt == null
                         && t.HiddenAt == null
                         && t.Status == TrackStatus.Ready
                         && t.Visibility == ContentVisibility.Public
                         && t.Owner.DeletedAt == null
                         && t.Owner.Status == UserStatus.Active);

    /// <summary>
    /// Morceaux visibles par l'appelant : les morceaux publiquement référencés, auxquels
    /// s'ajoutent ses propres morceaux, et la totalité du catalogue pour la modération.
    /// </summary>
    public static IQueryable<Track> VisibleTo(this IQueryable<Track> query, Guid? viewerId, UserRole viewerRole)
    {
        if (viewerRole is UserRole.Moderator or UserRole.Admin)
        {
            return query.Where(t => t.DeletedAt == null);
        }

        if (viewerId is null)
        {
            return query.PubliclyListed();
        }

        var ownerId = viewerId.Value;
        return query.Where(t => t.DeletedAt == null
                                && (t.OwnerId == ownerId
                                    || (t.HiddenAt == null
                                        && t.Status == TrackStatus.Ready
                                        && t.Visibility == ContentVisibility.Public
                                        && t.Owner.DeletedAt == null
                                        && t.Owner.Status == UserStatus.Active)));
    }

    /// <summary>Projette les morceaux vers <see cref="TrackProjection"/> en une seule requête SQL.</summary>
    public static IQueryable<TrackProjection> Project(this IQueryable<Track> query, Guid? viewerId) =>
        query.Select(t => new TrackProjection
        {
            Id = t.Id,
            Title = t.Title,
            ArtistName = t.ArtistName,
            DurationSeconds = t.DurationSeconds,
            Visibility = t.Visibility,
            Status = t.Status,
            LikeCount = t.LikeCount,
            PlayCount = t.PlayCount,
            OwnerId = t.OwnerId,
            OwnerUsername = t.Owner.Username,
            OwnerAvatarFileId = t.Owner.AvatarFileId,
            OwnerShowLikeCount = t.Owner.Settings.ShowLikeCount,
            OwnerShowPlayCount = t.Owner.Settings.ShowPlayCount,
            GenreId = t.GenreId,
            GenreName = t.Genre != null ? t.Genre.Name : null,
            GenreSlug = t.Genre != null ? t.Genre.Slug : null,
            AlbumId = t.AlbumId,
            AlbumName = t.Album != null ? t.Album.Name : null,
            AlbumArtistName = t.Album != null ? t.Album.ArtistName : null,
            Tags = t.TrackTags.Select(tt => tt.Tag.Name).ToList(),
            Description = t.Description,
            Year = t.Year,
            FailureReason = t.FailureReason,
            HiddenAt = t.HiddenAt,
            CreatedAt = t.CreatedAt,
            PublishedAt = t.PublishedAt,
            IsLiked = viewerId != null && t.Likes.Any(l => l.UserId == viewerId),
        });

    /// <summary>Applique le tri demandé, avec un repli déterministe sur la date de création.</summary>
    public static IQueryable<Track> ApplySort(this IQueryable<Track> query, string? sort) => sort?.ToLowerInvariant() switch
    {
        "popular" => query.OrderByDescending(t => t.PlayCount).ThenByDescending(t => t.CreatedAt),
        "likes" => query.OrderByDescending(t => t.LikeCount).ThenByDescending(t => t.CreatedAt),
        "title" => query.OrderBy(t => t.Title).ThenBy(t => t.Id),
        "duration" => query.OrderBy(t => t.DurationSeconds).ThenBy(t => t.Id),
        "oldest" => query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
        _ => query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id),
    };
}

/// <summary>Conversion des projections vers les DTO exposés par l'API.</summary>
public static class TrackMapper
{
    /// <summary>Slug d'URL correspondant à une taille de pochette.</summary>
    public static string CoverSlug(CoverSize size) => size switch
    {
        CoverSize.Small => "small",
        CoverSize.Medium => "medium",
        CoverSize.Large => "large",
        _ => "original",
    };

    /// <summary>
    /// Construit le DTO d'un morceau en appliquant les préférences de visibilité des compteurs :
    /// un compteur masqué reste accessible au propriétaire et aux administrateurs.
    /// </summary>
    public static TrackDto ToDto(this TrackProjection source, Guid? viewerId, UserRole viewerRole)
    {
        ArgumentNullException.ThrowIfNull(source);

        var isPrivileged = viewerId == source.OwnerId || viewerRole == UserRole.Admin;

        return new TrackDto
        {
            Id = source.Id,
            Title = source.Title,
            ArtistName = source.ArtistName,
            DurationSeconds = source.DurationSeconds,
            Visibility = source.Visibility,
            Status = source.Status,
            Owner = new UserRefDto(source.OwnerId, source.OwnerUsername, MediaUrls.Avatar(source.OwnerAvatarFileId)),
            Genre = source.GenreId is null
                ? null
                : new GenreDto(source.GenreId.Value, source.GenreName ?? string.Empty, source.GenreSlug ?? string.Empty, null),
            Tags = source.Tags,
            CoverUrls = new CoverUrlsDto(
                MediaUrls.TrackCover(source.Id, "small"),
                MediaUrls.TrackCover(source.Id, "medium"),
                MediaUrls.TrackCover(source.Id, "large")),
            StreamUrl = MediaUrls.TrackStream(source.Id),
            LikeCount = isPrivileged || source.OwnerShowLikeCount ? source.LikeCount : null,
            PlayCount = isPrivileged || source.OwnerShowPlayCount ? source.PlayCount : null,
            IsLikedByCurrentUser = viewerId is null ? null : source.IsLiked,
            CreatedAt = source.CreatedAt,
            PublishedAt = source.PublishedAt,
        };
    }

    /// <summary>Construit le DTO détaillé, en n'exposant les champs internes qu'aux ayants droit.</summary>
    public static TrackDetailsDto ToDetailsDto(this TrackProjection source, Guid? viewerId, UserRole viewerRole, int commentCount)
    {
        ArgumentNullException.ThrowIfNull(source);

        var isPrivileged = viewerId == source.OwnerId || viewerRole is UserRole.Moderator or UserRole.Admin;

        return new TrackDetailsDto
        {
            Track = source.ToDto(viewerId, viewerRole),
            Description = source.Description,
            Year = source.Year,
            Album = source.AlbumId is null
                ? null
                : new AlbumDto(source.AlbumId.Value, source.AlbumName ?? string.Empty, source.AlbumArtistName ?? string.Empty, null),
            CommentCount = commentCount,
            FailureReason = isPrivileged ? source.FailureReason : null,
            IsHidden = isPrivileged ? source.HiddenAt is not null : null,
        };
    }

    /// <summary>Charge une page de morceaux visibles et la convertit en DTO.</summary>
    public static async Task<Common.PagedResult<TrackDto>> ToTrackPageAsync(
        this IQueryable<Track> query,
        Common.PageRequest page,
        Guid? viewerId,
        UserRole viewerRole,
        CancellationToken cancellationToken)
    {
        var total = await query.LongCountAsync(cancellationToken);
        if (total == 0)
        {
            return Common.PagedResult<TrackDto>.Empty(page.Page, page.PageSize);
        }

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Project(viewerId)
            .ToListAsync(cancellationToken);

        return new Common.PagedResult<TrackDto>
        {
            Items = items.Select(p => p.ToDto(viewerId, viewerRole)).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalItems = total,
        };
    }
}
