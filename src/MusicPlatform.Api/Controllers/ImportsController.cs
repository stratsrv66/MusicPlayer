using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Import;

namespace MusicPlatform.Api.Controllers;

/// <summary>
/// Import de playlists YouTube : aperçu, lancement, suivi de la progression, annulation
/// et relance. Chaque morceau est récupéré par yt-dlp exactement comme un morceau importé
/// depuis un lien YouTube isolé.
/// </summary>
[ApiController]
[Route("api/v1/imports")]
[Authorize]
public sealed class ImportsController(PlaylistImportService importService) : ApiControllerBase
{
    /// <summary>
    /// Décrit une playlist et ses morceaux sans rien importer, afin de vérifier le contenu
    /// et le nombre de morceaux avant de lancer l'opération.
    /// </summary>
    /// <param name="request"><c>url</c> : lien de la playlist YouTube, ou son identifiant.</param>
    /// <response code="200">Playlist et morceaux relevés.</response>
    /// <response code="400">Le lien ne désigne pas une playlist YouTube.</response>
    /// <response code="422">La playlist est illisible, privée ou inexistante.</response>
    /// <response code="503">yt-dlp n'est pas installé sur le serveur.</response>
    [HttpPost("playlists/preview")]
    [EnableRateLimiting(RateLimitPolicies.Search)]
    [ProducesResponseType<PlaylistPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlaylistPreviewDto>> Preview(
        PreviewPlaylistRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await importService.PreviewAsync(request.Url, cancellationToken));
    }

    /// <summary>Liste les playlists publiques d'une chaîne YouTube.</summary>
    /// <param name="profileId">
    /// Pseudonyme <c>@handle</c> de la chaîne, ou URL complète de sa page.
    /// </param>
    /// <response code="200">Playlists publiques de la chaîne.</response>
    /// <response code="400">Identifiant de chaîne absent.</response>
    /// <response code="422">La chaîne est introuvable.</response>
    [HttpGet("playlists/profile")]
    [EnableRateLimiting(RateLimitPolicies.Search)]
    [ProducesResponseType<IReadOnlyList<ExternalPlaylistDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<ExternalPlaylistDto>>> ProfilePlaylists(
        [FromQuery] string profileId,
        CancellationToken cancellationToken) =>
        Ok(await importService.ListProfilePlaylistsAsync(profileId, cancellationToken));

    /// <summary>
    /// Inventorie la playlist puis programme son import en arrière-plan.
    ///
    /// Les morceaux déjà présents dans la bibliothèque sont rattachés sans être
    /// retéléchargés.
    /// </summary>
    /// <param name="request">
    /// <c>url</c> (obligatoire), <c>visibility</c> (<c>PUBLIC</c>, <c>UNLISTED</c> ou
    /// <c>PRIVATE</c>, défaut <c>PRIVATE</c>) et <c>createPlaylist</c> (booléen, défaut vrai).
    /// </param>
    /// <response code="202">Import accepté et programmé.</response>
    /// <response code="400">Le lien ne désigne pas une playlist YouTube.</response>
    /// <response code="409">Cette playlist est déjà en cours d'import.</response>
    /// <response code="422">Playlist vide, illisible ou au-delà de la limite de morceaux.</response>
    /// <response code="503">yt-dlp n'est pas installé sur le serveur.</response>
    [HttpPost("playlists")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [ProducesResponseType<PlaylistImportDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlaylistImportDto>> Start(
        StartPlaylistImportRequest request,
        CancellationToken cancellationToken)
    {
        var import = await importService.StartAsync(request, cancellationToken);
        return Accepted($"/api/v1/imports/playlists/{import.Id}", import);
    }

    /// <summary>Liste les imports les plus récents de l'appelant.</summary>
    /// <response code="200">Imports et leur progression.</response>
    [HttpGet("playlists")]
    [ProducesResponseType<IReadOnlyList<PlaylistImportDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaylistImportDto>>> List(CancellationToken cancellationToken) =>
        Ok(await importService.ListAsync(cancellationToken));

    /// <summary>
    /// Retourne un import, sa progression et l'état de chacun de ses morceaux.
    /// C'est l'endpoint interrogé périodiquement pendant l'import pour suivre l'avancement.
    /// </summary>
    /// <param name="importId">Identifiant de l'import.</param>
    /// <response code="200">Import et détail des morceaux.</response>
    /// <response code="404">Import inexistant ou appartenant à un autre utilisateur.</response>
    [HttpGet("playlists/{importId:guid}")]
    [ProducesResponseType<PlaylistImportDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaylistImportDetailsDto>> Get(Guid importId, CancellationToken cancellationToken) =>
        Ok(await importService.GetAsync(importId, cancellationToken));

    /// <summary>
    /// Annule un import en cours. Le morceau en cours de téléchargement va à son terme ;
    /// les suivants sont abandonnés et peuvent être relancés ensuite.
    /// </summary>
    /// <param name="importId">Identifiant de l'import.</param>
    /// <response code="200">Import annulé.</response>
    /// <response code="404">Import inexistant ou appartenant à un autre utilisateur.</response>
    /// <response code="422">L'import est déjà terminé.</response>
    [HttpPost("playlists/{importId:guid}/cancel")]
    [ProducesResponseType<PlaylistImportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlaylistImportDto>> Cancel(Guid importId, CancellationToken cancellationToken) =>
        Ok(await importService.CancelAsync(importId, cancellationToken));

    /// <summary>
    /// Relance les morceaux en échec ou annulés d'un import.
    /// Les morceaux déjà importés ne sont jamais retraités.
    /// </summary>
    /// <param name="importId">Identifiant de l'import.</param>
    /// <response code="200">Import reprogrammé.</response>
    /// <response code="404">Import inexistant ou appartenant à un autre utilisateur.</response>
    /// <response code="422">Aucun morceau à relancer.</response>
    [HttpPost("playlists/{importId:guid}/retry")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [ProducesResponseType<PlaylistImportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlaylistImportDto>> Retry(Guid importId, CancellationToken cancellationToken) =>
        Ok(await importService.RetryFailedAsync(importId, cancellationToken));
}
