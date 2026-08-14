using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Playlists;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Application.Features.Users;

namespace MusicPlatform.Application.Features.Discovery;

/// <summary>Compose la page d'accueil à partir des différentes sources de découverte.</summary>
public sealed class HomeService(IAppDbContext db, ICurrentUser currentUser, RecommendationService recommendations)
{
    /// <summary>Nombre d'éléments affichés dans chaque carrousel de la page d'accueil.</summary>
    private const int SectionSize = 12;

    /// <summary>Construit le contenu de la page d'accueil pour l'appelant.</summary>
    public async Task<HomeDto> GetAsync(CancellationToken cancellationToken)
    {
        var viewerId = currentUser.UserId;

        var recentTracks = await LoadTracksAsync(
            db.Tracks.AsNoTracking().PubliclyListed().ApplySort("recent"),
            cancellationToken);

        var popularTracks = await recommendations.GetPopularAsync(SectionSize, cancellationToken);
        var recommended = await recommendations.GetTrackRecommendationsAsync(SectionSize, cancellationToken);

        var popularArtists = await db.Users
            .AsNoTracking()
            .Active()
            .Where(u => u.ProfileVisibility == Domain.Enums.ProfileVisibility.Public
                        && u.Tracks.Any(t => t.DeletedAt == null
                                             && t.HiddenAt == null
                                             && t.Status == Domain.Enums.TrackStatus.Ready
                                             && t.Visibility == Domain.Enums.ContentVisibility.Public))
            .OrderByDescending(u => u.Followers.Count)
            .ThenByDescending(u => u.Tracks.Sum(t => t.PlayCount))
            .ThenBy(u => u.Id)
            .Take(SectionSize)
            .ProjectSummary()
            .ToListAsync(cancellationToken);

        var popularPlaylists = await db.Playlists
            .AsNoTracking()
            .Where(p => p.Visibility == Domain.Enums.ContentVisibility.Public
                        && p.Owner.DeletedAt == null
                        && p.Items.Count > 0)
            .OrderByDescending(p => p.Follows.Count)
            .ThenByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.Id)
            .Take(SectionSize)
            .Project(viewerId)
            .ToListAsync(cancellationToken);

        var fromFollowed = viewerId is null
            ? []
            : await LoadTracksAsync(
                db.Tracks
                    .AsNoTracking()
                    .PubliclyListed()
                    .Where(t => t.Owner.Followers.Any(f => f.FollowerId == viewerId))
                    .ApplySort("recent"),
                cancellationToken);

        return new HomeDto
        {
            RecentTracks = recentTracks,
            PopularTracks = popularTracks,
            PopularArtists = popularArtists,
            PopularPlaylists = popularPlaylists,
            Recommendations = recommended,
            FromFollowedArtists = fromFollowed,
        };
    }

    /// <summary>Charge une section de morceaux déjà triée et la convertit en DTO.</summary>
    private async Task<IReadOnlyList<TrackDto>> LoadTracksAsync(IQueryable<Domain.Entities.Track> query, CancellationToken cancellationToken)
    {
        var projections = await query
            .Take(SectionSize)
            .Project(currentUser.UserId)
            .ToListAsync(cancellationToken);

        return projections.Select(p => p.ToDto(currentUser.UserId, currentUser.Role)).ToList();
    }
}
