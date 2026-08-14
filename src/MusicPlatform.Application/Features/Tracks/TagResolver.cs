using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Application.Features.Tracks;

/// <summary>
/// Résout les libellés de tags saisis par l'utilisateur en entités <see cref="Tag"/>,
/// en créant les tags manquants, puis synchronise les associations d'un morceau.
/// </summary>
public sealed class TagResolver(IAppDbContext db, IClock clock)
{
    /// <summary>Nombre maximal de tags acceptés pour un morceau.</summary>
    public const int MaxTagsPerTrack = 20;

    /// <summary>Longueur maximale du slug d'un tag.</summary>
    public const int MaxTagLength = 50;

    /// <summary>Nombre de tentatives en cas de création concurrente du même tag.</summary>
    private const int MaxCreateAttempts = 2;

    /// <summary>
    /// Remplace l'ensemble des tags d'un morceau par ceux fournis. Les libellés sont
    /// normalisés, dédupliqués, et les tags inconnus sont créés.
    /// </summary>
    public async Task ApplyAsync(Track track, IReadOnlyList<string>? rawTags, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);

        var slugs = NormalizeAll(rawTags);
        var tags = slugs.Count == 0 ? [] : await ResolveAsync(slugs, cancellationToken);

        var desired = tags.ToDictionary(t => t.Id);
        var existing = track.TrackTags.ToList();

        foreach (var association in existing)
        {
            if (!desired.Remove(association.TagId))
            {
                track.TrackTags.Remove(association);
                db.TrackTags.Remove(association);
            }
        }

        foreach (var tagId in desired.Keys)
        {
            track.TrackTags.Add(new TrackTag { TrackId = track.Id, TagId = tagId });
        }
    }

    /// <summary>Normalise, déduplique et borne la liste de tags fournie.</summary>
    public static List<string> NormalizeAll(IReadOnlyList<string>? rawTags)
    {
        if (rawTags is null || rawTags.Count == 0)
        {
            return [];
        }

        var slugs = new List<string>(Math.Min(rawTags.Count, MaxTagsPerTrack));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < rawTags.Count && slugs.Count < MaxTagsPerTrack; i++)
        {
            var slug = Tag.Normalize(rawTags[i]);
            if (slug.Length == 0)
            {
                continue;
            }

            if (slug.Length > MaxTagLength)
            {
                throw new InputValidationException("tags", $"A tag cannot exceed {MaxTagLength} characters.");
            }

            if (seen.Add(slug))
            {
                slugs.Add(slug);
            }
        }

        return slugs;
    }

    /// <summary>Charge les tags existants et crée ceux qui manquent, en tolérant les créations concurrentes.</summary>
    private async Task<List<Tag>> ResolveAsync(List<string> slugs, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            var existing = await db.Tags.Where(t => slugs.Contains(t.Slug)).ToListAsync(cancellationToken);
            var missing = slugs.Except(existing.Select(t => t.Slug), StringComparer.Ordinal).ToList();

            if (missing.Count == 0)
            {
                return existing;
            }

            var created = missing
                .Select(slug => new Tag { Name = slug, Slug = slug, CreatedAt = clock.UtcNow })
                .ToList();

            db.Tags.AddRange(created);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                existing.AddRange(created);
                return existing;
            }
            catch (DbUpdateException) when (attempt < MaxCreateAttempts)
            {
                // Un autre appel a créé le même tag entre-temps : on relit et on réessaie une fois.
                foreach (var tag in created)
                {
                    db.Tags.Remove(tag);
                }
            }
        }

        // Après la dernière tentative, les tags existent forcément : on les relit.
        return await db.Tags.Where(t => slugs.Contains(t.Slug)).ToListAsync(cancellationToken);
    }
}
