using Microsoft.EntityFrameworkCore;

namespace MusicPlatform.Application.Common;

/// <summary>Page de résultats renvoyée par toutes les collections de l'API.</summary>
/// <typeparam name="T">Type des éléments de la page.</typeparam>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalItems { get; init; }

    /// <summary>Nombre total de pages, au minimum zéro lorsque la collection est vide.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)((TotalItems + PageSize - 1) / PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalItems = 0 };
}

/// <summary>Paramètres de pagination normalisés et bornés.</summary>
public sealed record PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    /// <summary>Numéro de page à partir de 1. Toute valeur inférieure est ramenée à 1.</summary>
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>Taille de page bornée à <see cref="MaxPageSize"/> pour éviter les requêtes non bornées.</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>Nombre d'éléments à ignorer pour atteindre la page demandée.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Helpers de pagination sur les requêtes EF Core.</summary>
public static class QueryablePagingExtensions
{
    /// <summary>
    /// Exécute une requête paginée : un <c>COUNT</c> puis la page demandée.
    /// La projection est appliquée avant l'appel afin de ne jamais sélectionner de colonnes inutiles.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);

        var total = await query.LongCountAsync(cancellationToken);
        if (total == 0)
        {
            return PagedResult<T>.Empty(page.Page, page.PageSize);
        }

        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalItems = total,
        };
    }

    /// <summary>Transforme les éléments d'une page déjà chargée sans relancer de requête.</summary>
    public static PagedResult<TOut> Map<TIn, TOut>(this PagedResult<TIn> source, Func<TIn, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        return new PagedResult<TOut>
        {
            Items = source.Items.Select(selector).ToList(),
            Page = source.Page,
            PageSize = source.PageSize,
            TotalItems = source.TotalItems,
        };
    }
}
