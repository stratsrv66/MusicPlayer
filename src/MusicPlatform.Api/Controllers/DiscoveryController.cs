using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Catalog;
using MusicPlatform.Application.Features.Discovery;
using MusicPlatform.Application.Features.Moderation;
using MusicPlatform.Application.Features.Search;
using MusicPlatform.Application.Features.Tracks;

namespace MusicPlatform.Api.Controllers;

/// <summary>Recherche transverse sur les morceaux, utilisateurs, albums, playlists et tags.</summary>
[ApiController]
[Route("api/v1/search")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Search)]
public sealed class SearchController(ISearchService searchService) : ApiControllerBase
{
    /// <summary>
    /// Recherche multi-type. Un terme préfixé par <c>#</c> est interprété comme un tag.
    /// </summary>
    /// <param name="query">
    /// Paramètres : q, type, genre, tag, artist, minDuration, maxDuration, sort, page, pageSize.
    /// </param>
    /// <response code="200">Résultats par type. Les sections non demandées sont nulles.</response>
    [HttpGet]
    [ProducesResponseType<SearchResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchResultDto>> Search([FromQuery] SearchQuery query, CancellationToken cancellationToken) =>
        Ok(await searchService.SearchAsync(query, cancellationToken));
}

/// <summary>Page d'accueil et recommandations.</summary>
[ApiController]
[Route("api/v1")]
[AllowAnonymous]
public sealed class DiscoveryController(HomeService homeService, RecommendationService recommendationService) : ApiControllerBase
{
    /// <summary>
    /// Contenu de la page d'accueil : nouveautés, populaires, artistes et playlists en vue,
    /// recommandations et publications des artistes suivis.
    /// </summary>
    /// <response code="200">Sections de la page d'accueil.</response>
    [HttpGet("home")]
    [ProducesResponseType<HomeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HomeDto>> Home(CancellationToken cancellationToken) =>
        Ok(await homeService.GetAsync(cancellationToken));

    /// <summary>Morceaux recommandés. Pour un visiteur anonyme, la popularité récente sert de repli.</summary>
    /// <param name="limit">Nombre de morceaux, entre 1 et 50.</param>
    /// <response code="200">Liste ordonnée par score décroissant.</response>
    [HttpGet("recommendations/tracks")]
    [ProducesResponseType<IReadOnlyList<TrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> Tracks(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await recommendationService.GetTrackRecommendationsAsync(limit, cancellationToken));

    /// <summary>Artistes recommandés, hors artistes déjà suivis.</summary>
    /// <param name="limit">Nombre d'artistes, entre 1 et 50.</param>
    /// <response code="200">Liste ordonnée par popularité.</response>
    [HttpGet("recommendations/artists")]
    [ProducesResponseType<IReadOnlyList<UserSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> Artists(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await recommendationService.GetArtistRecommendationsAsync(limit, cancellationToken));
}

/// <summary>Genres et tags du catalogue.</summary>
[ApiController]
[Route("api/v1")]
[AllowAnonymous]
public sealed class CatalogController(CatalogService catalogService, TrackService trackService) : ApiControllerBase
{
    /// <summary>Liste des genres avec le nombre de morceaux publics.</summary>
    /// <response code="200">Genres triés par nom.</response>
    [HttpGet("genres")]
    [ProducesResponseType<IReadOnlyList<GenreDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> Genres(CancellationToken cancellationToken) =>
        Ok(await catalogService.ListGenresAsync(cancellationToken));

    /// <summary>Morceaux publics d'un genre.</summary>
    /// <param name="genreId">Identifiant du genre.</param>
    /// <param name="sort">Ordre de tri.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de morceaux.</response>
    /// <response code="404">Genre inexistant.</response>
    [HttpGet("genres/{genreId:guid}/tracks")]
    [ProducesResponseType<PagedResultDto<TrackDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResultDto<TrackDto>>> GenreTracks(
        Guid genreId,
        [FromQuery] string? sort,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await trackService.ListByGenreAsync(genreId, page.ToPageRequest(), sort, cancellationToken));

    /// <summary>Recherche de tags, triés par popularité.</summary>
    /// <param name="q">Fragment de tag, avec ou sans <c>#</c>.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de tags portés par au moins un morceau public.</response>
    [HttpGet("tags")]
    [EnableRateLimiting(RateLimitPolicies.Search)]
    [ProducesResponseType<PagedResultDto<TagDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<TagDto>>> Tags(
        [FromQuery] string? q,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await catalogService.ListTagsAsync(q, page.ToPageRequest(), cancellationToken));

    /// <summary>Morceaux publics portant un tag.</summary>
    /// <param name="tag">Tag, avec ou sans <c>#</c>.</param>
    /// <param name="sort">Ordre de tri.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de morceaux.</response>
    [HttpGet("tags/{tag}/tracks")]
    [ProducesResponseType<PagedResultDto<TrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<TrackDto>>> TagTracks(
        string tag,
        [FromQuery] string? sort,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await trackService.ListByTagAsync(tag, page.ToPageRequest(), sort, cancellationToken));

    /// <summary>Détail d'un album.</summary>
    /// <param name="albumId">Identifiant de l'album.</param>
    /// <response code="200">Album trouvé.</response>
    /// <response code="404">Album inexistant.</response>
    [HttpGet("albums/{albumId:guid}")]
    [ProducesResponseType<AlbumDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlbumDto>> Album(Guid albumId, CancellationToken cancellationToken) =>
        Ok(await catalogService.GetAlbumAsync(albumId, cancellationToken));
}

/// <summary>Signalement de contenus par les utilisateurs.</summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Write)]
public sealed class ReportsController(ReportService reportService) : ApiControllerBase
{
    /// <summary>Signale un morceau, un commentaire, un profil ou une playlist.</summary>
    /// <param name="request">Type de cible, identifiant, motif et description.</param>
    /// <response code="201">Signalement enregistré.</response>
    /// <response code="404">Le contenu signalé n'existe pas.</response>
    [HttpPost]
    [ProducesResponseType<ReportDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportDto>> Create(CreateReportRequest request, CancellationToken cancellationToken)
    {
        var report = await reportService.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/me/reports", report);
    }
}
