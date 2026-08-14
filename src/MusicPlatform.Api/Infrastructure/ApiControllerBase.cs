using Microsoft.AspNetCore.Mvc;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;

namespace MusicPlatform.Api.Infrastructure;

/// <summary>Paramètres de pagination reçus en query string.</summary>
public sealed class PageQuery
{
    /// <summary>Numéro de page, à partir de 1.</summary>
    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    /// <summary>Nombre d'éléments par page, borné côté serveur à 100.</summary>
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; set; } = PageRequest.DefaultPageSize;

    /// <summary>Convertit en requête de pagination normalisée et bornée.</summary>
    public PageRequest ToPageRequest() => new() { Page = Page, PageSize = PageSize };
}

/// <summary>Base commune aux contrôleurs : conversion des pages et des flux de médias.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Durée de mise en cache navigateur des médias immuables.</summary>
    private const int MediaCacheSeconds = 86400;

    /// <summary>Convertit une page interne en sa forme sérialisée.</summary>
    protected static PagedResultDto<T> Page<T>(PagedResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PagedResultDto<T>(result.Items, result.Page, result.PageSize, result.TotalItems, result.TotalPages);
    }

    /// <summary>
    /// Renvoie un flux binaire avec support natif des requêtes HTTP <c>Range</c>.
    /// ASP.NET Core produit alors 206, 416 et les en-têtes <c>Content-Range</c> /
    /// <c>Accept-Ranges</c> à partir du flux positionnable fourni, sans le charger en mémoire.
    /// </summary>
    protected FileStreamResult StreamMedia(MediaStream media, string? downloadFileName = null)
    {
        ArgumentNullException.ThrowIfNull(media);

        Response.Headers.CacheControl = $"private, max-age={MediaCacheSeconds}";

        return new FileStreamResult(media.Content, media.ContentType)
        {
            EnableRangeProcessing = true,
            EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(media.ETag),
            LastModified = media.LastModified,
            FileDownloadName = downloadFileName,
        };
    }
}
