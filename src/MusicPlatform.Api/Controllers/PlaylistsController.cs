using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Playlists;

namespace MusicPlatform.Api.Controllers;

/// <summary>Playlists : création, contenu, réordonnancement, duplication, abonnement et favoris.</summary>
[ApiController]
[Route("api/v1/playlists")]
public sealed class PlaylistsController(PlaylistService playlistService) : ApiControllerBase
{
    /// <summary>Playlists visibles par l'appelant.</summary>
    /// <param name="ownerId">Restreint à un propriétaire donné.</param>
    /// <param name="sort"><c>recent</c>, <c>popular</c> ou <c>name</c>.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de playlists.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResultDto<PlaylistDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<PlaylistDto>>> List(
        [FromQuery] Guid? ownerId,
        [FromQuery] string? sort,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await playlistService.ListAsync(ownerId, page.ToPageRequest(), sort, cancellationToken));

    /// <summary>Détail d'une playlist et ses morceaux ordonnés.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <response code="200">Playlist accessible.</response>
    /// <response code="404">Playlist inexistante ou privée.</response>
    [HttpGet("{playlistId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<PlaylistDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaylistDetailsDto>> Get(Guid playlistId, CancellationToken cancellationToken) =>
        Ok(await playlistService.GetAsync(playlistId, cancellationToken));

    /// <summary>Crée une playlist.</summary>
    /// <param name="request">Nom, description et visibilité.</param>
    /// <response code="201">Playlist créée.</response>
    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaylistDto>> Create(CreatePlaylistRequest request, CancellationToken cancellationToken)
    {
        var playlist = await playlistService.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/playlists/{playlist.Id}", playlist);
    }

    /// <summary>Met à jour une playlist.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <param name="request">Champs à modifier.</param>
    /// <response code="200">Playlist mise à jour.</response>
    /// <response code="403">L'appelant n'est pas propriétaire.</response>
    [HttpPatch("{playlistId:guid}")]
    [Authorize]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlaylistDto>> Update(
        Guid playlistId,
        UpdatePlaylistRequest request,
        CancellationToken cancellationToken) =>
        Ok(await playlistService.UpdateAsync(playlistId, request, cancellationToken));

    /// <summary>Supprime une playlist.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <response code="204">Playlist supprimée.</response>
    /// <response code="403">L'appelant n'est pas propriétaire.</response>
    [HttpDelete("{playlistId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid playlistId, CancellationToken cancellationToken)
    {
        await playlistService.DeleteAsync(playlistId, cancellationToken);
        return NoContent();
    }

    /// <summary>Remplace la pochette de la playlist.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <param name="file">Image de 5 Mo maximum.</param>
    /// <response code="200">Playlist mise à jour.</response>
    [HttpPost("{playlistId:guid}/cover")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaylistDto>> SetCover(Guid playlistId, IFormFile file, CancellationToken cancellationToken) =>
        Ok(await playlistService.SetCoverAsync(playlistId, await file.ToUploadedImageAsync(cancellationToken), cancellationToken));

    /// <summary>Morceaux d'une playlist, dans l'ordre.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <response code="200">Liste ordonnée des morceaux.</response>
    [HttpGet("{playlistId:guid}/tracks")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<PlaylistTrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaylistTrackDto>>> ListTracks(Guid playlistId, CancellationToken cancellationToken)
    {
        var details = await playlistService.GetAsync(playlistId, cancellationToken);
        return Ok(details.Tracks);
    }

    /// <summary>Ajoute un morceau à la fin de la playlist.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <param name="request">Identifiant du morceau.</param>
    /// <response code="200">Playlist mise à jour.</response>
    /// <response code="409">Le morceau est déjà présent, ou la playlist est pleine.</response>
    [HttpPost("{playlistId:guid}/tracks")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlaylistDto>> AddTrack(
        Guid playlistId,
        AddPlaylistTrackRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await playlistService.AddTrackAsync(playlistId, request.TrackId, cancellationToken));
    }

    /// <summary>Retire un morceau et compacte les positions restantes.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">Playlist mise à jour.</response>
    /// <response code="404">Le morceau n'est pas dans la playlist.</response>
    [HttpDelete("{playlistId:guid}/tracks/{trackId:guid}")]
    [Authorize]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaylistDto>> RemoveTrack(Guid playlistId, Guid trackId, CancellationToken cancellationToken) =>
        Ok(await playlistService.RemoveTrackAsync(playlistId, trackId, cancellationToken));

    /// <summary>
    /// Applique un nouvel ordre. La liste doit couvrir tous les morceaux de la playlist,
    /// avec des positions contiguës à partir de zéro.
    /// </summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <param name="request">Couples morceau/position.</param>
    /// <response code="200">Playlist réordonnée.</response>
    /// <response code="422">Positions incomplètes, dupliquées ou non contiguës.</response>
    [HttpPatch("{playlistId:guid}/tracks/reorder")]
    [Authorize]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlaylistDto>> Reorder(
        Guid playlistId,
        ReorderPlaylistRequest request,
        CancellationToken cancellationToken) =>
        Ok(await playlistService.ReorderAsync(playlistId, request, cancellationToken));

    /// <summary>Duplique une playlist visible dans le compte de l'appelant.</summary>
    /// <param name="playlistId">Identifiant de la playlist source.</param>
    /// <param name="request">Nom et visibilité de la copie.</param>
    /// <response code="201">Copie créée.</response>
    [HttpPost("{playlistId:guid}/duplicate")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaylistDto>> Duplicate(
        Guid playlistId,
        DuplicatePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        var copy = await playlistService.DuplicateAsync(playlistId, request, cancellationToken);
        return Created($"/api/v1/playlists/{copy.Id}", copy);
    }

    /// <summary>Suit une playlist. L'opération est idempotente.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <response code="200">Playlist mise à jour.</response>
    [HttpPost("{playlistId:guid}/follow")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaylistDto>> Follow(Guid playlistId, CancellationToken cancellationToken) =>
        Ok(await playlistService.FollowAsync(playlistId, cancellationToken));

    /// <summary>Cesse de suivre une playlist. L'opération est idempotente.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <response code="200">Playlist mise à jour.</response>
    [HttpDelete("{playlistId:guid}/follow")]
    [Authorize]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaylistDto>> Unfollow(Guid playlistId, CancellationToken cancellationToken) =>
        Ok(await playlistService.UnfollowAsync(playlistId, cancellationToken));

    /// <summary>Ajoute la playlist aux favoris. L'opération est idempotente.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <response code="200">Playlist mise à jour.</response>
    [HttpPost("{playlistId:guid}/favorite")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaylistDto>> Favorite(Guid playlistId, CancellationToken cancellationToken) =>
        Ok(await playlistService.FavoriteAsync(playlistId, cancellationToken));

    /// <summary>Retire la playlist des favoris. L'opération est idempotente.</summary>
    /// <param name="playlistId">Identifiant de la playlist.</param>
    /// <response code="200">Playlist mise à jour.</response>
    [HttpDelete("{playlistId:guid}/favorite")]
    [Authorize]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaylistDto>> Unfavorite(Guid playlistId, CancellationToken cancellationToken) =>
        Ok(await playlistService.UnfavoriteAsync(playlistId, cancellationToken));
}

/// <summary>Modification et suppression des commentaires.</summary>
[ApiController]
[Route("api/v1/comments")]
[Authorize]
public sealed class CommentsController(Application.Features.Comments.CommentService commentService) : ApiControllerBase
{
    /// <summary>Modifie le texte d'un commentaire. Seul l'auteur en a le droit.</summary>
    /// <param name="commentId">Identifiant du commentaire.</param>
    /// <param name="request">Nouveau texte.</param>
    /// <response code="200">Commentaire modifié.</response>
    /// <response code="403">L'appelant n'est pas l'auteur.</response>
    [HttpPatch("{commentId:guid}")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CommentDto>> Update(
        Guid commentId,
        UpdateCommentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await commentService.UpdateAsync(commentId, request, cancellationToken));

    /// <summary>
    /// Supprime un commentaire. L'auteur, le propriétaire du morceau et la modération
    /// en ont le droit.
    /// </summary>
    /// <param name="commentId">Identifiant du commentaire.</param>
    /// <response code="204">Commentaire supprimé.</response>
    /// <response code="403">L'appelant n'a pas le droit de le supprimer.</response>
    [HttpDelete("{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid commentId, CancellationToken cancellationToken)
    {
        await commentService.DeleteAsync(commentId, cancellationToken);
        return NoContent();
    }
}
