using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Account;
using MusicPlatform.Application.Features.Analytics;
using MusicPlatform.Application.Features.Moderation;
using MusicPlatform.Application.Features.Playback;
using MusicPlatform.Application.Features.Playlists;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Application.Features.Users;

namespace MusicPlatform.Api.Controllers;

/// <summary>Compte de l'utilisateur connecté : profil, préférences, bibliothèque, statistiques et données.</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(
    UserService userService,
    AccountService accountService,
    LikeService likeService,
    PlaybackService playbackService,
    TrackService trackService,
    PlaylistService playlistService,
    AnalyticsService analyticsService,
    ReportService reportService) : ApiControllerBase
{
    /// <summary>Profil complet de l'utilisateur connecté.</summary>
    /// <response code="200">Profil, email et préférences.</response>
    [HttpGet]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeDto>> Get(CancellationToken cancellationToken) =>
        Ok(await userService.GetMeAsync(cancellationToken));

    /// <summary>Met à jour le profil : pseudo, bio, liens sociaux et visibilité.</summary>
    /// <param name="request">Champs à modifier ; les champs absents sont conservés.</param>
    /// <response code="200">Profil mis à jour.</response>
    /// <response code="409">Pseudo déjà utilisé.</response>
    [HttpPatch]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MeDto>> Update(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await userService.UpdateProfileAsync(request, cancellationToken));

    /// <summary>Remplace l'avatar.</summary>
    /// <param name="file">Image de 5 Mo maximum.</param>
    /// <response code="200">Profil mis à jour avec le nouvel avatar.</response>
    [HttpPost("avatar")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeDto>> SetAvatar(IFormFile file, CancellationToken cancellationToken) =>
        Ok(await userService.SetAvatarAsync(await file.ToUploadedImageAsync(cancellationToken), cancellationToken));

    /// <summary>Supprime l'avatar.</summary>
    /// <response code="200">Profil mis à jour.</response>
    [HttpDelete("avatar")]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeDto>> DeleteAvatar(CancellationToken cancellationToken) =>
        Ok(await userService.RemoveAvatarAsync(cancellationToken));

    /// <summary>Préférences d'affichage des compteurs.</summary>
    /// <response code="200">Préférences courantes.</response>
    [HttpGet("settings")]
    [ProducesResponseType<UserSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserSettingsDto>> GetSettings(CancellationToken cancellationToken) =>
        Ok(await userService.GetSettingsAsync(cancellationToken));

    /// <summary>Met à jour les préférences d'affichage des compteurs.</summary>
    /// <param name="request">Champs <c>showLikeCount</c> et <c>showPlayCount</c>.</param>
    /// <response code="200">Préférences mises à jour.</response>
    [HttpPatch("settings")]
    [ProducesResponseType<UserSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings(UpdateSettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await userService.UpdateSettingsAsync(request, cancellationToken));

    /// <summary>Morceaux de l'utilisateur connecté, y compris privés et en cours de traitement.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de morceaux.</response>
    [HttpGet("tracks")]
    [ProducesResponseType<PagedResultDto<TrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<TrackDto>>> ListTracks([FromQuery] PageQuery page, CancellationToken cancellationToken)
    {
        var me = await userService.GetMeAsync(cancellationToken);
        return Page(await trackService.ListByUserAsync(me.Profile.Username, page.ToPageRequest(), cancellationToken));
    }

    /// <summary>Playlists de l'utilisateur connecté.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de playlists.</response>
    [HttpGet("playlists")]
    [ProducesResponseType<PagedResultDto<PlaylistDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<PlaylistDto>>> ListPlaylists([FromQuery] PageQuery page, CancellationToken cancellationToken)
    {
        var me = await userService.GetMeAsync(cancellationToken);
        return Page(await playlistService.ListAsync(me.Profile.Id, page.ToPageRequest(), null, cancellationToken));
    }

    /// <summary>Playlists mises en favori.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de playlists.</response>
    [HttpGet("favorites")]
    [ProducesResponseType<PagedResultDto<PlaylistDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<PlaylistDto>>> ListFavorites([FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        Page(await playlistService.ListFavoritesAsync(page.ToPageRequest(), cancellationToken));

    /// <summary>Morceaux aimés.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de morceaux.</response>
    [HttpGet("likes")]
    [ProducesResponseType<PagedResultDto<TrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<TrackDto>>> ListLikes([FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        Page(await likeService.ListLikedAsync(page.ToPageRequest(), cancellationToken));

    /// <summary>Historique d'écoute, du plus récent au plus ancien.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'entrées d'historique avec la position de reprise.</response>
    [HttpGet("history")]
    [ProducesResponseType<PagedResultDto<HistoryEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<HistoryEntryDto>>> ListHistory([FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        Page(await playbackService.GetHistoryAsync(page.ToPageRequest(), cancellationToken));

    /// <summary>Efface l'intégralité de l'historique d'écoute.</summary>
    /// <response code="204">Historique effacé.</response>
    [HttpDelete("history")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearHistory(CancellationToken cancellationToken)
    {
        await playbackService.ClearHistoryAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Abonnés de l'utilisateur connecté.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'utilisateurs.</response>
    [HttpGet("followers")]
    [ProducesResponseType<PagedResultDto<UserSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<UserSummaryDto>>> ListFollowers([FromQuery] PageQuery page, CancellationToken cancellationToken)
    {
        var me = await userService.GetMeAsync(cancellationToken);
        return Page(await userService.ListFollowersAsync(me.Profile.Id, page.ToPageRequest(), cancellationToken));
    }

    /// <summary>Utilisateurs suivis par l'utilisateur connecté.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'utilisateurs.</response>
    [HttpGet("following")]
    [ProducesResponseType<PagedResultDto<UserSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<UserSummaryDto>>> ListFollowing([FromQuery] PageQuery page, CancellationToken cancellationToken)
    {
        var me = await userService.GetMeAsync(cancellationToken);
        return Page(await userService.ListFollowingAsync(me.Profile.Id, page.ToPageRequest(), cancellationToken));
    }

    /// <summary>Chiffres clés du tableau de bord artiste.</summary>
    /// <response code="200">Écoutes, likes, abonnés et nombre de morceaux.</response>
    [HttpGet("analytics/overview")]
    [ProducesResponseType<AnalyticsOverviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnalyticsOverviewDto>> AnalyticsOverview(CancellationToken cancellationToken) =>
        Ok(await analyticsService.GetOverviewAsync(cancellationToken));

    /// <summary>Statistiques détaillées par morceau.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de statistiques.</response>
    [HttpGet("analytics/tracks")]
    [ProducesResponseType<PagedResultDto<TrackAnalyticsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<TrackAnalyticsDto>>> AnalyticsTracks(
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await analyticsService.GetTrackAnalyticsAsync(page.ToPageRequest(), cancellationToken));

    /// <summary>Série temporelle des écoutes.</summary>
    /// <param name="from">Début de la période, incluse. Par défaut trente jours en arrière.</param>
    /// <param name="to">Fin de la période, incluse. Par défaut aujourd'hui.</param>
    /// <param name="groupBy">Granularité : <c>Day</c>, <c>Week</c> ou <c>Month</c>.</param>
    /// <response code="200">Série d'écoutes agrégée.</response>
    /// <response code="400">Période invalide ou supérieure à 366 jours.</response>
    [HttpGet("analytics/plays")]
    [ProducesResponseType<PlaysSeriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaysSeriesDto>> AnalyticsPlays(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] AnalyticsGroupBy groupBy = AnalyticsGroupBy.Day,
        CancellationToken cancellationToken = default) =>
        Ok(await analyticsService.GetPlaysSeriesAsync(from, to, groupBy, cancellationToken));

    /// <summary>Morceaux les plus écoutés de l'utilisateur connecté.</summary>
    /// <param name="limit">Nombre de morceaux, entre 1 et 50.</param>
    /// <response code="200">Liste triée par écoutes décroissantes.</response>
    [HttpGet("analytics/top-tracks")]
    [ProducesResponseType<IReadOnlyList<TrackAnalyticsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrackAnalyticsDto>>> TopTracks(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        Ok(await analyticsService.GetTopTracksAsync(limit, cancellationToken));

    /// <summary>Signalements émis par l'utilisateur connecté.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de signalements.</response>
    [HttpGet("reports")]
    [ProducesResponseType<PagedResultDto<ReportDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<ReportDto>>> ListReports([FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        Page(await reportService.ListMineAsync(page.ToPageRequest(), cancellationToken));

    /// <summary>Demande la génération d'une archive contenant les données personnelles.</summary>
    /// <response code="202">Demande enregistrée, génération en arrière-plan.</response>
    /// <response code="409">Un export est déjà en cours.</response>
    [HttpPost("data-export")]
    [ProducesResponseType<UserExportDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserExportDto>> RequestExport(CancellationToken cancellationToken)
    {
        var export = await accountService.RequestExportAsync(cancellationToken);
        return Accepted($"/api/v1/me/data-exports/{export.Id}", export);
    }

    /// <summary>Liste les demandes d'export.</summary>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'exports.</response>
    [HttpGet("data-exports")]
    [ProducesResponseType<PagedResultDto<UserExportDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<UserExportDto>>> ListExports([FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        Page(await accountService.ListExportsAsync(page.ToPageRequest(), cancellationToken));

    /// <summary>État d'une demande d'export.</summary>
    /// <param name="exportId">Identifiant de l'export.</param>
    /// <response code="200">État courant.</response>
    /// <response code="404">Export inexistant ou appartenant à un autre compte.</response>
    [HttpGet("data-exports/{exportId:guid}")]
    [ProducesResponseType<UserExportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserExportDto>> GetExport(Guid exportId, CancellationToken cancellationToken) =>
        Ok(await accountService.GetExportAsync(exportId, cancellationToken));

    /// <summary>Télécharge l'archive d'un export disponible.</summary>
    /// <param name="exportId">Identifiant de l'export.</param>
    /// <response code="200">Archive ZIP.</response>
    /// <response code="422">L'export n'est pas prêt ou a expiré.</response>
    [HttpGet("data-exports/{exportId:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DownloadExport(Guid exportId, CancellationToken cancellationToken) =>
        StreamMedia(await accountService.DownloadExportAsync(exportId, cancellationToken), $"musicplatform-export-{exportId}.zip");

    /// <summary>
    /// Supprime définitivement le compte. La confirmation explicite et la saisie exacte
    /// du pseudo sont toutes deux obligatoires.
    /// </summary>
    /// <param name="request">Confirmation et pseudo du compte.</param>
    /// <response code="204">Compte supprimé.</response>
    /// <response code="422">Confirmation absente ou pseudo incorrect.</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        await accountService.DeleteOwnAccountAsync(request, cancellationToken);
        return NoContent();
    }
}
