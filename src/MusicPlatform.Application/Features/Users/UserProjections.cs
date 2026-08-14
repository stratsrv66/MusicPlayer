using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Users;

/// <summary>Forme intermédiaire d'un profil, remplie par une projection SQL unique.</summary>
public sealed class UserProjection
{
    public Guid Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Bio { get; init; }
    public Guid? AvatarFileId { get; init; }
    public string? SocialLinks { get; init; }
    public ProfileVisibility ProfileVisibility { get; init; }
    public UserRole Role { get; init; }
    public UserStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
    public int TrackCount { get; init; }
    public int PlaylistCount { get; init; }
    public int FollowerCount { get; init; }
    public int FollowingCount { get; init; }
    public bool IsFollowedByViewer { get; init; }
    public bool ShowLikeCount { get; init; }
    public bool ShowPlayCount { get; init; }
}

/// <summary>Projections et conversions relatives aux utilisateurs.</summary>
public static class UserQueries
{
    /// <summary>Utilisateurs visibles publiquement : ni supprimés, ni suspendus.</summary>
    public static IQueryable<User> Active(this IQueryable<User> query) =>
        query.Where(u => u.DeletedAt == null && u.Status == UserStatus.Active);

    /// <summary>
    /// Projette les utilisateurs avec leurs compteurs. Les morceaux comptabilisés sont
    /// uniquement les morceaux publiquement visibles, sauf pour le profil de l'appelant.
    /// </summary>
    public static IQueryable<UserProjection> Project(this IQueryable<User> query, Guid? viewerId) =>
        query.Select(u => new UserProjection
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            Bio = u.Bio,
            AvatarFileId = u.AvatarFileId,
            SocialLinks = u.SocialLinks,
            ProfileVisibility = u.ProfileVisibility,
            Role = u.Role,
            Status = u.Status,
            CreatedAt = u.CreatedAt,
            DeletedAt = u.DeletedAt,
            TrackCount = u.Tracks.Count(t => t.DeletedAt == null
                                             && (u.Id == viewerId
                                                 || (t.HiddenAt == null
                                                     && t.Status == TrackStatus.Ready
                                                     && t.Visibility == ContentVisibility.Public))),
            PlaylistCount = u.Playlists.Count(p => u.Id == viewerId || p.Visibility == ContentVisibility.Public),
            FollowerCount = u.Followers.Count,
            FollowingCount = u.Following.Count,
            IsFollowedByViewer = viewerId != null && u.Followers.Any(f => f.FollowerId == viewerId),
            ShowLikeCount = u.Settings.ShowLikeCount,
            ShowPlayCount = u.Settings.ShowPlayCount,
        });

    /// <summary>Projette une référence légère vers l'utilisateur, sans compteur.</summary>
    public static IQueryable<UserSummaryDto> ProjectSummary(this IQueryable<User> query) =>
        query.Select(u => new UserSummaryDto(
            u.Id,
            u.Username,
            u.AvatarFileId == null ? null : MediaUrls.Base + "/media/avatars/" + u.AvatarFileId,
            u.Followers.Count,
            u.Tracks.Count(t => t.DeletedAt == null
                                && t.HiddenAt == null
                                && t.Status == TrackStatus.Ready
                                && t.Visibility == ContentVisibility.Public)));
}

/// <summary>Conversion des projections utilisateur vers les DTO exposés.</summary>
public static class UserMapper
{
    private static readonly JsonSerializerOptions SocialLinksOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Construit le profil public. Un profil privé consulté par un tiers est renvoyé
    /// « restreint » : seules l'identité et l'image restent visibles.
    /// </summary>
    public static UserProfileDto ToProfileDto(this UserProjection source, Guid? viewerId, UserRole viewerRole)
    {
        ArgumentNullException.ThrowIfNull(source);

        var isSelf = viewerId is not null && viewerId == source.Id;
        var isPrivileged = isSelf || viewerRole is UserRole.Moderator or UserRole.Admin;
        var isRestricted = !isPrivileged && source.ProfileVisibility == ProfileVisibility.Private;

        if (isRestricted)
        {
            return new UserProfileDto
            {
                Id = source.Id,
                Username = source.Username,
                AvatarUrl = MediaUrls.Avatar(source.AvatarFileId),
                ProfileVisibility = source.ProfileVisibility,
                Role = source.Role,
                CreatedAt = source.CreatedAt,
                TrackCount = 0,
                PlaylistCount = 0,
                FollowerCount = 0,
                FollowingCount = 0,
                IsFollowedByCurrentUser = viewerId is null ? null : source.IsFollowedByViewer,
                IsRestricted = true,
            };
        }

        return new UserProfileDto
        {
            Id = source.Id,
            Username = source.Username,
            Bio = source.Bio,
            AvatarUrl = MediaUrls.Avatar(source.AvatarFileId),
            SocialLinks = ParseSocialLinks(source.SocialLinks),
            ProfileVisibility = source.ProfileVisibility,
            Role = source.Role,
            CreatedAt = source.CreatedAt,
            TrackCount = source.TrackCount,
            PlaylistCount = source.PlaylistCount,
            FollowerCount = source.FollowerCount,
            FollowingCount = source.FollowingCount,
            IsFollowedByCurrentUser = viewerId is null ? null : source.IsFollowedByViewer,
            IsRestricted = false,
        };
    }

    /// <summary>Construit la vue privée du compte de l'utilisateur connecté.</summary>
    public static MeDto ToMeDto(this UserProjection source) => new()
    {
        Profile = source.ToProfileDto(source.Id, source.Role),
        Email = source.Email,
        Settings = new UserSettingsDto(source.ShowLikeCount, source.ShowPlayCount),
        Status = source.Status,
    };

    /// <summary>Désérialise les liens sociaux stockés en JSON, en ignorant un contenu corrompu.</summary>
    public static IReadOnlyDictionary<string, string>? ParseSocialLinks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SocialLinksOptions);
        }
        catch (JsonException)
        {
            // Une valeur illisible ne doit pas empêcher l'affichage du profil.
            return null;
        }
    }

    /// <summary>Sérialise les liens sociaux vers leur forme de stockage.</summary>
    public static string? SerializeSocialLinks(IReadOnlyDictionary<string, string>? links) =>
        links is null || links.Count == 0 ? null : JsonSerializer.Serialize(links, SocialLinksOptions);
}
