using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Catalog;

/// <summary>Consultation des genres, tags et albums.</summary>
public sealed class CatalogService(IAppDbContext db)
{
    /// <summary>Liste tous les genres avec le nombre de morceaux publics associés.</summary>
    public async Task<IReadOnlyList<GenreDto>> ListGenresAsync(CancellationToken cancellationToken) =>
        await db.Genres
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenreDto(g.Id, g.Name, g.Slug, g.Tracks.Count(t => t.DeletedAt == null
                                                                                && t.HiddenAt == null
                                                                                && t.Status == TrackStatus.Ready
                                                                                && t.Visibility == ContentVisibility.Public)))
            .ToListAsync(cancellationToken);

    /// <summary>Recherche paginée de tags, triés par popularité.</summary>
    public async Task<PagedResult<TagDto>> ListTagsAsync(string? query, PageRequest page, CancellationToken cancellationToken)
    {
        var tags = db.Tags.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = SqlPatterns.Contains(Tag.Normalize(query));
            tags = tags.Where(t => EF.Functions.Like(t.Slug, pattern, SqlPatterns.EscapeCharacter));
        }

        // Le filtre et le tri portent sur l'entité, jamais sur une propriété du DTO :
        // une condition appliquée après la projection ne serait pas traduisible en SQL.
        var projected = tags
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

        return await projected.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Retourne un album et son nombre de morceaux publics.</summary>
    public async Task<AlbumDto> GetAlbumAsync(Guid albumId, CancellationToken cancellationToken) =>
        await db.Albums
            .AsNoTracking()
            .Where(a => a.Id == albumId)
            .Select(a => new AlbumDto(
                a.Id,
                a.Name,
                a.ArtistName,
                a.Tracks.Count(t => t.DeletedAt == null
                                    && t.HiddenAt == null
                                    && t.Status == TrackStatus.Ready
                                    && t.Visibility == ContentVisibility.Public)))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(ErrorCodes.AlbumNotFound, "The requested album does not exist.");
}
