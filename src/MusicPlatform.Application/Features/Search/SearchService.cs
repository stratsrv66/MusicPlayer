using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Playlists;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Application.Features.Users;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Search;

/// <summary>
/// Contrat de recherche. L'implémentation par défaut interroge PostgreSQL ;
/// une implémentation OpenSearch pourra être substituée sans modifier les appelants.
/// </summary>
public interface ISearchService
{
    Task<SearchResultDto> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Recherche adossée à PostgreSQL. Chaque type de contenu est interrogé séparément ;
/// une requête préfixée par <c>#</c> est traitée comme une recherche par tag.
/// </summary>
public sealed class PostgresSearchService(IAppDbContext db, ICurrentUser currentUser) : ISearchService
{
    /// <summary>Nombre de résultats par type lorsqu'une recherche globale est demandée.</summary>
    private const int MixedResultsPerType = 6;

    /// <inheritdoc />
    public async Task<SearchResultDto> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = query.Q?.Trim() ?? string.Empty;
        var isTagSearch = term.StartsWith('#');
        var page = new PageRequest
        {
            Page = query.Page,
            PageSize = query.Type == SearchType.All ? MixedResultsPerType : query.PageSize,
        };

        var result = new SearchResultDto { Type = query.Type, Query = query.Q };

        var wantsTracks = query.Type is SearchType.All or SearchType.Track;
        var wantsUsers = query.Type is SearchType.All or SearchType.User;
        var wantsAlbums = query.Type is SearchType.All or SearchType.Album;
        var wantsPlaylists = query.Type is SearchType.All or SearchType.Playlist;
        var wantsTags = query.Type is SearchType.All or SearchType.Tag;

        return result with
        {
            Tracks = wantsTracks ? ToDto(await SearchTracksAsync(query, term, isTagSearch, page, cancellationToken)) : null,
            Users = wantsUsers && !isTagSearch ? ToDto(await SearchUsersAsync(term, page, cancellationToken)) : null,
            Albums = wantsAlbums && !isTagSearch ? ToDto(await SearchAlbumsAsync(term, page, cancellationToken)) : null,
            Playlists = wantsPlaylists && !isTagSearch ? ToDto(await SearchPlaylistsAsync(term, page, cancellationToken)) : null,
            Tags = wantsTags ? ToDto(await SearchTagsAsync(term, page, cancellationToken)) : null,
        };
    }

    /// <summary>Recherche les morceaux publics correspondant au terme et aux filtres.</summary>
    private async Task<PagedResult<TrackDto>> SearchTracksAsync(
        SearchQuery query,
        string term,
        bool isTagSearch,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var filter = new TrackFilter
        {
            Query = isTagSearch ? term : null,
            Genre = query.Genre,
            Tag = query.Tag,
            Artist = query.Artist,
            MinDuration = query.MinDuration,
            MaxDuration = query.MaxDuration,
            Sort = query.Sort,
        };

        var tracks = TrackService.ApplyFilter(db.Tracks.AsNoTracking().PubliclyListed(), filter);

        if (!isTagSearch && term.Length > 0)
        {
            var pattern = SqlPatterns.Contains(term);
            tracks = tracks.Where(t => EF.Functions.Like(t.Title.ToLower(), pattern, SqlPatterns.EscapeCharacter)
                                       || EF.Functions.Like(t.ArtistName.ToLower(), pattern, SqlPatterns.EscapeCharacter)
                                       || EF.Functions.Like(t.Owner.Username.ToLower(), pattern, SqlPatterns.EscapeCharacter)
                                       || t.TrackTags.Any(tt => EF.Functions.Like(tt.Tag.Slug, pattern, SqlPatterns.EscapeCharacter)));
        }

        return await tracks
            .ApplySort(query.Sort)
            .ToTrackPageAsync(page, currentUser.UserId, currentUser.Role, cancellationToken);
    }

    /// <summary>Recherche les utilisateurs par pseudo, en excluant les profils privés.</summary>
    private async Task<PagedResult<UserSummaryDto>> SearchUsersAsync(string term, PageRequest page, CancellationToken cancellationToken)
    {
        if (term.Length == 0)
        {
            return PagedResult<UserSummaryDto>.Empty(page.Page, page.PageSize);
        }

        var pattern = SqlPatterns.Contains(term);
        var users = db.Users
            .AsNoTracking()
            .Active()
            .Where(u => u.ProfileVisibility == ProfileVisibility.Public
                        && EF.Functions.Like(u.UsernameNormalized, pattern, SqlPatterns.EscapeCharacter))
            .OrderByDescending(u => u.Followers.Count)
            .ThenBy(u => u.UsernameNormalized)
            .ProjectSummary();

        return await users.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Recherche les albums ayant au moins un morceau public.</summary>
    private async Task<PagedResult<AlbumDto>> SearchAlbumsAsync(string term, PageRequest page, CancellationToken cancellationToken)
    {
        if (term.Length == 0)
        {
            return PagedResult<AlbumDto>.Empty(page.Page, page.PageSize);
        }

        var pattern = SqlPatterns.Contains(term);
        var albums = db.Albums
            .AsNoTracking()
            .Where(a => (EF.Functions.Like(a.Name.ToLower(), pattern, SqlPatterns.EscapeCharacter) || EF.Functions.Like(a.ArtistName.ToLower(), pattern, SqlPatterns.EscapeCharacter))
                        && a.Tracks.Any(t => t.DeletedAt == null
                                             && t.HiddenAt == null
                                             && t.Status == TrackStatus.Ready
                                             && t.Visibility == ContentVisibility.Public))
            .OrderBy(a => a.Name)
            .Select(a => new AlbumDto(
                a.Id,
                a.Name,
                a.ArtistName,
                a.Tracks.Count(t => t.DeletedAt == null
                                    && t.HiddenAt == null
                                    && t.Status == TrackStatus.Ready
                                    && t.Visibility == ContentVisibility.Public)));

        return await albums.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Recherche les playlists publiques.</summary>
    private async Task<PagedResult<PlaylistDto>> SearchPlaylistsAsync(string term, PageRequest page, CancellationToken cancellationToken)
    {
        if (term.Length == 0)
        {
            return PagedResult<PlaylistDto>.Empty(page.Page, page.PageSize);
        }

        var pattern = SqlPatterns.Contains(term);
        var playlists = db.Playlists
            .AsNoTracking()
            .Where(p => p.Visibility == ContentVisibility.Public
                        && p.Owner.DeletedAt == null
                        && EF.Functions.Like(p.Name.ToLower(), pattern, SqlPatterns.EscapeCharacter))
            .OrderByDescending(p => p.Follows.Count)
            .ThenBy(p => p.Name)
            .Project(currentUser.UserId);

        return await playlists.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Recherche les tags, en ne retenant que ceux portés par des morceaux publics.</summary>
    private async Task<PagedResult<TagDto>> SearchTagsAsync(string term, PageRequest page, CancellationToken cancellationToken)
    {
        var slug = Tag.Normalize(term);
        var query = db.Tags.AsNoTracking().AsQueryable();

        if (slug.Length > 0)
        {
            var pattern = SqlPatterns.Contains(slug);
            query = query.Where(t => EF.Functions.Like(t.Slug, pattern, SqlPatterns.EscapeCharacter));
        }

        // Le filtre et le tri portent sur l'entité, jamais sur une propriété du DTO :
        // une condition appliquée après la projection ne serait pas traduisible en SQL.
        var tags = query
            .Where(t => t.TrackTags.Any(tt => tt.Track.DeletedAt == null
                                              && tt.Track.HiddenAt == null
                                              && tt.Track.Status == TrackStatus.Ready
                                              && tt.Track.Visibility == ContentVisibility.Public))
            .OrderByDescending(t => t.TrackTags.Count(tt => tt.Track.DeletedAt == null
                                                            && tt.Track.HiddenAt == null
                                                            && tt.Track.Status == TrackStatus.Ready
                                                            && tt.Track.Visibility == ContentVisibility.Public))
            .ThenBy(t => t.Slug)
            .Select(t => new TagDto(
                t.Id,
                t.Name,
                t.Slug,
                t.TrackTags.Count(tt => tt.Track.DeletedAt == null
                                        && tt.Track.HiddenAt == null
                                        && tt.Track.Status == TrackStatus.Ready
                                        && tt.Track.Visibility == ContentVisibility.Public)));

        return await tags.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Convertit une page interne en sa forme sérialisable.</summary>
    private static PagedResultDto<T> ToDto<T>(PagedResult<T> source) =>
        new(source.Items, source.Page, source.PageSize, source.TotalItems, source.TotalPages);
}
