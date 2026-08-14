using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Comments;
using MusicPlatform.Application.Features.Playback;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Api.Controllers;

/// <summary>Morceaux : catalogue, upload, streaming, pochettes, likes, écoutes et commentaires.</summary>
[ApiController]
[Route("api/v1/tracks")]
public sealed class TracksController(
    TrackService trackService,
    TrackStreamService streamService,
    TrackCoverService coverService,
    LikeService likeService,
    PlaybackService playbackService,
    CommentService commentService) : ApiControllerBase
{
    /// <summary>Liste paginée des morceaux publics.</summary>
    /// <param name="filter">Filtres : q, genre, tag, artist, minDuration, maxDuration, from, to, sort.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de morceaux.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResultDto<TrackDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<TrackDto>>> List(
        [FromQuery] TrackFilterQuery filter,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await trackService.ListAsync(filter.ToFilter(), page.ToPageRequest(), cancellationToken));

    /// <summary>Détail d'un morceau.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">Morceau trouvé et accessible.</response>
    /// <response code="404">Morceau inexistant ou non accessible.</response>
    [HttpGet("{trackId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<TrackDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrackDetailsDto>> Get(Guid trackId, CancellationToken cancellationToken) =>
        Ok(await trackService.GetAsync(trackId, cancellationToken));

    /// <summary>Crée un morceau et envoie son fichier audio (multipart/form-data).</summary>
    /// <param name="form">Fichier audio et métadonnées initiales.</param>
    /// <response code="202">Upload accepté, traitement en cours.</response>
    /// <response code="413">Fichier au-delà de 20 Mo.</response>
    /// <response code="415">Format audio non pris en charge.</response>
    /// <response code="422">Le contenu n'est pas un fichier audio valide.</response>
    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(Track.MaxAudioFileSizeBytes + 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UploadAcceptedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UploadAcceptedDto>> Create([FromForm] CreateTrackForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var result = await trackService.CreateAsync(form.ToRequest(), form.File.ToUploadedFile(), cancellationToken);
        return Accepted($"/api/v1/tracks/{result.TrackId}", result);
    }

    /// <summary>Remplace le fichier audio d'un morceau existant.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="file">Nouveau fichier audio.</param>
    /// <response code="202">Upload accepté, traitement en cours.</response>
    /// <response code="409">Un upload est déjà en cours pour ce morceau.</response>
    [HttpPost("{trackId:guid}/upload")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(Track.MaxAudioFileSizeBytes + 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UploadAcceptedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UploadAcceptedDto>> Upload(Guid trackId, IFormFile file, CancellationToken cancellationToken) =>
        Accepted(await trackService.ReplaceFileAsync(trackId, file.ToUploadedFile(), cancellationToken));

    /// <summary>Diffuse le fichier audio. Les requêtes <c>Range</c> sont prises en charge.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">Flux complet.</response>
    /// <response code="206">Fragment demandé via l'en-tête Range.</response>
    /// <response code="416">Plage demandée hors limites.</response>
    /// <response code="404">Morceau inexistant ou non accessible.</response>
    [HttpGet("{trackId:guid}/stream")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stream(Guid trackId, CancellationToken cancellationToken) =>
        StreamMedia(await streamService.OpenAsync(trackId, cancellationToken));

    /// <summary>Met à jour les métadonnées d'un morceau.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="request">Champs à modifier ; les champs absents sont conservés.</param>
    /// <response code="200">Morceau mis à jour.</response>
    /// <response code="403">L'appelant n'est pas propriétaire du morceau.</response>
    [HttpPatch("{trackId:guid}")]
    [Authorize]
    [ProducesResponseType<TrackDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TrackDetailsDto>> Update(Guid trackId, UpdateTrackRequest request, CancellationToken cancellationToken) =>
        Ok(await trackService.UpdateAsync(trackId, request, cancellationToken));

    /// <summary>Supprime un morceau et ses fichiers.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="204">Morceau supprimé.</response>
    /// <response code="403">L'appelant n'est pas propriétaire du morceau.</response>
    [HttpDelete("{trackId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid trackId, CancellationToken cancellationToken)
    {
        await trackService.DeleteAsync(trackId, cancellationToken);
        return NoContent();
    }

    /// <summary>Publie un morceau prêt à l'écoute.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">Morceau publié.</response>
    /// <response code="422">Le traitement du fichier n'est pas terminé.</response>
    [HttpPost("{trackId:guid}/publish")]
    [Authorize]
    [ProducesResponseType<TrackDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TrackDetailsDto>> Publish(Guid trackId, CancellationToken cancellationToken) =>
        Ok(await trackService.PublishAsync(trackId, cancellationToken));

    /// <summary>Retire un morceau de la publication.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">Morceau dépublié.</response>
    [HttpPost("{trackId:guid}/unpublish")]
    [Authorize]
    [ProducesResponseType<TrackDetailsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TrackDetailsDto>> Unpublish(Guid trackId, CancellationToken cancellationToken) =>
        Ok(await trackService.UnpublishAsync(trackId, cancellationToken));

    /// <summary>Remplace la pochette du morceau par une image fournie.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="file">Image JPEG, PNG, WebP, GIF ou BMP de 5 Mo maximum.</param>
    /// <response code="204">Pochette remplacée.</response>
    /// <response code="415">Format d'image non pris en charge.</response>
    [HttpPost("{trackId:guid}/cover")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> SetCover(Guid trackId, IFormFile file, CancellationToken cancellationToken)
    {
        await coverService.ReplaceAsync(trackId, await file.ToUploadedImageAsync(cancellationToken), cancellationToken);
        return NoContent();
    }

    /// <summary>Supprime la pochette du morceau.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="204">Pochette supprimée.</response>
    [HttpDelete("{trackId:guid}/cover")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCover(Guid trackId, CancellationToken cancellationToken)
    {
        await coverService.RemoveAsync(trackId, cancellationToken);
        return NoContent();
    }

    /// <summary>Retourne la pochette dans la taille demandée.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="size">Taille : <c>small</c>, <c>medium</c> ou <c>large</c>.</param>
    /// <response code="200">Image WebP.</response>
    /// <response code="404">Le morceau n'a pas de pochette.</response>
    [HttpGet("{trackId:guid}/cover/{size}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCover(Guid trackId, string size, CancellationToken cancellationToken) =>
        StreamMedia(await coverService.OpenAsync(trackId, size, cancellationToken));

    /// <summary>Ajoute un like sur le morceau. L'opération est idempotente.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">État du like après l'opération.</response>
    [HttpPost("{trackId:guid}/like")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<LikeStateDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LikeStateDto>> Like(Guid trackId, CancellationToken cancellationToken) =>
        Ok(await likeService.LikeAsync(trackId, cancellationToken));

    /// <summary>Retire le like. L'opération est idempotente.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">État du like après l'opération.</response>
    [HttpDelete("{trackId:guid}/like")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<LikeStateDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LikeStateDto>> Unlike(Guid trackId, CancellationToken cancellationToken) =>
        Ok(await likeService.UnlikeAsync(trackId, cancellationToken));

    /// <summary>Retourne l'état du like de l'appelant.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">État du like et compteur si visible.</response>
    [HttpGet("{trackId:guid}/like")]
    [AllowAnonymous]
    [ProducesResponseType<LikeStateDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LikeStateDto>> GetLike(Guid trackId, CancellationToken cancellationToken) =>
        Ok(await likeService.GetStateAsync(trackId, cancellationToken));

    /// <summary>
    /// Déclare une écoute. Le serveur décide seul si elle est comptabilisée :
    /// au moins dix secondes écoutées et aucune écoute déjà comptée récemment.
    /// </summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="request">Session, position et durée écoutée.</param>
    /// <response code="200">Résultat de la prise en compte.</response>
    [HttpPost("{trackId:guid}/plays")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<RegisterPlayResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RegisterPlayResultDto>> RegisterPlay(
        Guid trackId,
        RegisterPlayRequest request,
        CancellationToken cancellationToken) =>
        Ok(await playbackService.RegisterPlayAsync(trackId, request, cancellationToken));

    /// <summary>Sauvegarde la position de lecture courante.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="request">Position en secondes.</param>
    /// <response code="200">Position enregistrée.</response>
    [HttpPut("{trackId:guid}/progress")]
    [Authorize]
    [ProducesResponseType<PlaybackProgressDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaybackProgressDto>> SaveProgress(
        Guid trackId,
        SaveProgressRequest request,
        CancellationToken cancellationToken) =>
        Ok(await playbackService.SaveProgressAsync(trackId, request, cancellationToken));

    /// <summary>Retourne la dernière position d'écoute connue.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <response code="200">Position enregistrée, ou zéro si inconnue.</response>
    [HttpGet("{trackId:guid}/progress")]
    [Authorize]
    [ProducesResponseType<PlaybackProgressDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaybackProgressDto>> GetProgress(Guid trackId, CancellationToken cancellationToken) =>
        Ok(await playbackService.GetProgressAsync(trackId, cancellationToken));

    /// <summary>Liste paginée des commentaires du morceau.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="page">Pagination.</param>
    /// <response code="200">Page de commentaires.</response>
    [HttpGet("{trackId:guid}/comments")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResultDto<CommentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<CommentDto>>> ListComments(
        Guid trackId,
        [FromQuery] PageQuery page,
        CancellationToken cancellationToken) =>
        Page(await commentService.ListAsync(trackId, page.ToPageRequest(), cancellationToken));

    /// <summary>Poste un commentaire, éventuellement positionné dans le morceau.</summary>
    /// <param name="trackId">Identifiant du morceau.</param>
    /// <param name="request">Texte et timestamp optionnel.</param>
    /// <response code="201">Commentaire créé.</response>
    [HttpPost("{trackId:guid}/comments")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentDto>> CreateComment(
        Guid trackId,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        var comment = await commentService.CreateAsync(trackId, request, cancellationToken);
        return Created($"/api/v1/comments/{comment.Id}", comment);
    }
}

/// <summary>Filtres de listage reçus en query string.</summary>
public sealed class TrackFilterQuery
{
    /// <summary>Terme libre. Un terme préfixé par <c>#</c> déclenche une recherche par tag.</summary>
    public string? Q { get; set; }

    public string? Genre { get; set; }
    public string? Tag { get; set; }
    public string? Artist { get; set; }
    public int? MinDuration { get; set; }
    public int? MaxDuration { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary><c>recent</c>, <c>oldest</c>, <c>popular</c>, <c>likes</c>, <c>title</c> ou <c>duration</c>.</summary>
    public string? Sort { get; set; }

    /// <summary>Convertit vers le filtre applicatif.</summary>
    public TrackFilter ToFilter() => new()
    {
        Query = Q,
        Genre = Genre,
        Tag = Tag,
        Artist = Artist,
        MinDuration = MinDuration,
        MaxDuration = MaxDuration,
        From = From,
        To = To,
        Sort = Sort,
    };
}

/// <summary>Formulaire multipart de création d'un morceau.</summary>
public sealed class CreateTrackForm
{
    /// <summary>Fichier audio. Obligatoire.</summary>
    public IFormFile File { get; set; } = null!;

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ArtistName { get; set; }
    public Guid? AlbumId { get; set; }
    public Guid? GenreId { get; set; }
    public int? Year { get; set; }

    /// <summary><c>PUBLIC</c>, <c>UNLISTED</c> ou <c>PRIVATE</c>. Par défaut <c>PRIVATE</c>.</summary>
    public Domain.Enums.ContentVisibility Visibility { get; set; } = Domain.Enums.ContentVisibility.Private;

    /// <summary>Tags du morceau, envoyés en champs répétés <c>tags</c>.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Convertit vers la commande applicative.</summary>
    public CreateTrackRequest ToRequest() => new()
    {
        Title = Title,
        Description = Description,
        ArtistName = ArtistName,
        AlbumId = AlbumId,
        GenreId = GenreId,
        Year = Year,
        Visibility = Visibility,
        Tags = Tags,
    };
}
