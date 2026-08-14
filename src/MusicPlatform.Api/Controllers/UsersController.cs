using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Playlists;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Application.Features.Users;

namespace MusicPlatform.Api.Controllers;

/// <summary>Profils publics et relations d'abonnement.</summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(
    UserService userService,
    TrackService trackService,
    PlaylistService playlistService) : ApiControllerBase
{
    /// <summary>Profil public d'un utilisateur.</summary>
    /// <param name="username">Pseudo, insensible à la casse.</param>
    /// <response code="200">Profil. Les profils privés sont renvoyés en version restreinte.</response>
    /// <response code="404">Utilisateur inexistant.</response>
    [HttpGet("{username}")]
    [AllowAnonymous]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> Get(string username, CancellationToken cancellationToken) =>
        Ok(await userService.GetByUsernameAsync(username, cancellationToken));

    /// <summary>Morceaux publics d'un utilisateur.</summary>
    /// <param name="username">Pseudo de l'utilisateur.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de morceaux, vide si le profil est privé.</response>
    [HttpGet("{username}/tracks")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResultDto<TrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<TrackDto>>> ListTracks(
        string username,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await trackService.ListByUserAsync(username, page.ToPageRequest(), cancellationToken));

    /// <summary>Playlists publiques d'un utilisateur.</summary>
    /// <param name="username">Pseudo de l'utilisateur.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de playlists, vide si le profil est privé.</response>
    [HttpGet("{username}/playlists")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResultDto<PlaylistDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<PlaylistDto>>> ListPlaylists(
        string username,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await playlistService.ListByUsernameAsync(username, page.ToPageRequest(), cancellationToken));

    /// <summary>Suit un utilisateur. L'opération est idempotente.</summary>
    /// <param name="userId">Identifiant de l'utilisateur à suivre.</param>
    /// <response code="204">Abonnement effectif.</response>
    /// <response code="404">Utilisateur inexistant ou inactif.</response>
    /// <response code="422">Tentative d'auto-abonnement.</response>
    [HttpPost("{userId:guid}/follow")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Follow(Guid userId, CancellationToken cancellationToken)
    {
        await userService.FollowAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Cesse de suivre un utilisateur. L'opération est idempotente.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <response code="204">Abonnement retiré.</response>
    [HttpDelete("{userId:guid}/follow")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unfollow(Guid userId, CancellationToken cancellationToken)
    {
        await userService.UnfollowAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Abonnés d'un utilisateur.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'utilisateurs.</response>
    /// <response code="403">Le profil est privé.</response>
    [HttpGet("{userId:guid}/followers")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResultDto<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResultDto<UserSummaryDto>>> ListFollowers(
        Guid userId,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await userService.ListFollowersAsync(userId, page.ToPageRequest(), cancellationToken));

    /// <summary>Utilisateurs suivis par un utilisateur.</summary>
    /// <param name="userId">Identifiant de l'utilisateur.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page d'utilisateurs.</response>
    /// <response code="403">Le profil est privé.</response>
    [HttpGet("{userId:guid}/following")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResultDto<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResultDto<UserSummaryDto>>> ListFollowing(
        Guid userId,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await userService.ListFollowingAsync(userId, page.ToPageRequest(), cancellationToken));
}

/// <summary>Sert les images génériques : avatars et pochettes de playlists.</summary>
[ApiController]
[Route("api/v1/media")]
[AllowAnonymous]
public sealed class MediaController(UserService userService) : ApiControllerBase
{
    /// <summary>Retourne l'avatar correspondant à un identifiant de fichier.</summary>
    /// <param name="fileId">Identifiant du fichier stocké.</param>
    /// <response code="200">Image WebP.</response>
    /// <response code="404">Fichier inexistant.</response>
    [HttpGet("avatars/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Avatar(Guid fileId, CancellationToken cancellationToken) =>
        StreamMedia(await userService.OpenAvatarAsync(fileId, cancellationToken));

    /// <summary>Retourne la pochette d'une playlist.</summary>
    /// <param name="fileId">Identifiant du fichier stocké.</param>
    /// <response code="200">Image WebP.</response>
    /// <response code="404">Fichier inexistant.</response>
    [HttpGet("playlist-covers/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlaylistCover(Guid fileId, CancellationToken cancellationToken) =>
        StreamMedia(await userService.OpenPlaylistCoverAsync(fileId, cancellationToken));
}
