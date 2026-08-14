using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Account;
using MusicPlatform.Application.Features.Admin;
using MusicPlatform.Application.Features.Moderation;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Api.Controllers;

/// <summary>
/// Administration et modération. L'accès est doublement contrôlé : par la policy de
/// l'endpoint et par la vérification explicite du rôle dans la couche applicative.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AuthorizationPolicies.CanModerateContent)]
[EnableRateLimiting(RateLimitPolicies.Admin)]
public sealed class AdminController(
    AdminService adminService,
    ReportService reportService,
    AccountService accountService) : ApiControllerBase
{
    /// <summary>Liste paginée des utilisateurs.</summary>
    /// <param name="q">Fragment de pseudo ou d'email.</param>
    /// <param name="role">Filtre sur le rôle.</param>
    /// <param name="status">Filtre sur le statut.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'utilisateurs.</response>
    /// <response code="403">Privilèges administrateur requis.</response>
    [HttpGet("users")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType<PagedResultDto<AdminUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResultDto<AdminUserDto>>> ListUsers(
        [FromQuery] string? q,
        [FromQuery] UserRole? role,
        [FromQuery] UserStatus? status,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await adminService.ListUsersAsync(q, role, status, page.ToPageRequest(), cancellationToken));

    /// <summary>Détail administratif d'un utilisateur.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <response code="200">Utilisateur trouvé.</response>
    [HttpGet("users/{userId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUserDto>> GetUser(Guid userId, CancellationToken cancellationToken) =>
        Ok(await adminService.GetUserAsync(userId, cancellationToken));

    /// <summary>
    /// Modifie le rôle ou le statut d'un compte. Une suspension révoque immédiatement
    /// toutes les sessions actives du compte visé.
    /// </summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="request">Rôle et statut souhaités.</param>
    /// <response code="200">Utilisateur mis à jour.</response>
    /// <response code="409">Un administrateur ne peut pas révoquer ses propres accès.</response>
    [HttpPatch("users/{userId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> UpdateUser(
        Guid userId,
        UpdateAdminUserRequest request,
        CancellationToken cancellationToken) =>
        Ok(await adminService.UpdateUserAsync(userId, request, cancellationToken));

    /// <summary>Supprime administrativement un compte et l'ensemble de ses contenus.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <response code="204">Compte supprimé.</response>
    /// <response code="409">Utiliser l'endpoint personnel pour supprimer son propre compte.</response>
    [HttpDelete("users/{userId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUser(Guid userId, CancellationToken cancellationToken)
    {
        await adminService.DeleteUserAsync(userId, accountService, cancellationToken);
        return NoContent();
    }

    /// <summary>Liste globale des morceaux, y compris masqués.</summary>
    /// <param name="q">Fragment de titre, d'artiste ou de pseudo.</param>
    /// <param name="includeDeleted">Inclut les morceaux supprimés.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de morceaux.</response>
    [HttpGet("tracks")]
    [ProducesResponseType<PagedResultDto<AdminTrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<AdminTrackDto>>> ListTracks(
        [FromQuery] string? q,
        [FromQuery] bool includeDeleted,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await adminService.ListTracksAsync(q, includeDeleted, page.ToPageRequest(), cancellationToken));

    /// <summary>Masque un morceau sans le supprimer.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="204">Morceau masqué.</response>
    [HttpPost("tracks/{trackId:guid}/hide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> HideTrack(Guid trackId, CancellationToken cancellationToken)
    {
        await adminService.HideTrackAsync(trackId, cancellationToken);
        return NoContent();
    }

    /// <summary>Restaure un morceau masqué.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="204">Morceau restauré.</response>
    [HttpPost("tracks/{trackId:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RestoreTrack(Guid trackId, CancellationToken cancellationToken)
    {
        await adminService.RestoreTrackAsync(trackId, cancellationToken);
        return NoContent();
    }

    /// <summary>Supprime définitivement un morceau et ses fichiers.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="204">Morceau supprimé.</response>
    [HttpDelete("tracks/{trackId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTrack(Guid trackId, CancellationToken cancellationToken)
    {
        await adminService.DeleteTrackAsync(trackId, cancellationToken);
        return NoContent();
    }

    /// <summary>Liste filtrée des signalements.</summary>
    /// <param name="filter">Filtres : status, reason, targetType, from, to.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de signalements, les plus anciens en attente d'abord.</response>
    [HttpGet("reports")]
    [ProducesResponseType<PagedResultDto<ReportDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<ReportDto>>> ListReports(
        [FromQuery] ReportFilter filter,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await reportService.ListForModerationAsync(filter, page.ToPageRequest(), cancellationToken));

    /// <summary>Détail d'un signalement.</summary>
    /// <param name="reportId">Identifiant du signalement.</param>
    /// <response code="200">Signalement trouvé.</response>
    [HttpGet("reports/{reportId:guid}")]
    [ProducesResponseType<ReportDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportDto>> GetReport(Guid reportId, CancellationToken cancellationToken) =>
        Ok(await reportService.GetForModerationAsync(reportId, cancellationToken));

    /// <summary>
    /// Traite un signalement : nouveau statut, justification et masquage éventuel de la cible.
    /// L'action est tracée dans le journal d'audit.
    /// </summary>
    /// <param name="reportId">Identifiant du signalement.</param>
    /// <param name="request">Statut, note et demande de masquage.</param>
    /// <response code="200">Signalement traité.</response>
    /// <response code="400">Un signalement ne peut pas revenir à l'état PENDING.</response>
    [HttpPatch("reports/{reportId:guid}")]
    [ProducesResponseType<ReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReportDto>> ResolveReport(
        Guid reportId,
        ResolveReportRequest request,
        CancellationToken cancellationToken) =>
        Ok(await reportService.ResolveAsync(reportId, request, cancellationToken));

    /// <summary>Journal d'audit des actions d'administration.</summary>
    /// <param name="action">Fragment de nom d'action.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'entrées, les plus récentes d'abord.</response>
    [HttpGet("audit-logs")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType<PagedResultDto<AuditLogDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<AuditLogDto>>> ListAuditLogs(
        [FromQuery] string? action,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await adminService.ListAuditLogsAsync(action, page.ToPageRequest(), cancellationToken));

    /// <summary>Statistiques globales de la plateforme.</summary>
    /// <response code="200">Compteurs, stockage utilisé et écoutes des trente derniers jours.</response>
    [HttpGet("statistics")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType<AdminStatisticsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminStatisticsDto>> Statistics(CancellationToken cancellationToken) =>
        Ok(await adminService.GetStatisticsAsync(cancellationToken));

    /// <summary>Crée un genre.</summary>
    /// <param name="request">Nom du genre.</param>
    /// <response code="201">Genre créé.</response>
    /// <response code="409">Un genre du même nom existe déjà.</response>
    [HttpPost("genres")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType<GenreDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GenreDto>> CreateGenre(SaveGenreRequest request, CancellationToken cancellationToken)
    {
        var genre = await adminService.CreateGenreAsync(request, cancellationToken);
        return Created($"/api/v1/genres/{genre.Id}", genre);
    }

    /// <summary>Renomme un genre.</summary>
    /// <param name="genreId">Identifiant du genre.</param>
    /// <param name="request">Nouveau nom.</param>
    /// <response code="200">Genre mis à jour.</response>
    [HttpPatch("genres/{genreId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType<GenreDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GenreDto>> UpdateGenre(
        Guid genreId,
        SaveGenreRequest request,
        CancellationToken cancellationToken) =>
        Ok(await adminService.UpdateGenreAsync(genreId, request, cancellationToken));

    /// <summary>Supprime un genre qui n'est référencé par aucun morceau.</summary>
    /// <param name="genreId">Identifiant du genre.</param>
    /// <response code="204">Genre supprimé.</response>
    /// <response code="409">Le genre est encore utilisé.</response>
    [HttpDelete("genres/{genreId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CanAccessAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteGenre(Guid genreId, CancellationToken cancellationToken)
    {
        await adminService.DeleteGenreAsync(genreId, cancellationToken);
        return NoContent();
    }
}
