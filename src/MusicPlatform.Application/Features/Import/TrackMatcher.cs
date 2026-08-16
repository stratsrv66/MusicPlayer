using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Import;

/// <summary>Éléments permettant d'identifier un morceau, par fiabilité décroissante.</summary>
/// <param name="Platform">Plateforme d'origine du morceau.</param>
/// <param name="ExternalId">Identifiant du morceau chez cette plateforme.</param>
/// <param name="ArtistName">Nom d'artiste tel qu'annoncé.</param>
/// <param name="Title">Titre tel qu'annoncé.</param>
/// <param name="DurationSeconds">Durée annoncée, ou zéro si inconnue.</param>
public readonly record struct TrackIdentity(
    ExternalPlatform Platform,
    string? ExternalId,
    string? ArtistName,
    string? Title,
    int DurationSeconds);

/// <summary>
/// Rapproche un morceau externe des morceaux déjà présents dans la bibliothèque, afin
/// d'éviter de retélécharger et de dupliquer un morceau existant.
///
/// La recherche est toujours limitée à la bibliothèque du propriétaire : deux
/// utilisateurs peuvent légitimement détenir le même morceau.
///
/// Ordre de préférence : identifiant de la vidéo, puis clé « artiste|titre » normalisée,
/// la durée servant à départager plusieurs homonymes.
/// </summary>
public sealed class TrackMatcher(IAppDbContext db)
{
    /// <summary>Nombre d'homonymes examinés avant de renoncer au rapprochement.</summary>
    private const int MaxCandidates = 10;

    /// <summary>
    /// Retourne le morceau de la bibliothèque correspondant à <paramref name="identity"/>,
    /// ou <c>null</c> s'il n'en existe aucun.
    /// </summary>
    public async Task<Track?> FindAsync(Guid ownerId, TrackIdentity identity, CancellationToken cancellationToken = default)
    {
        var owned = db.Tracks.Where(t => t.OwnerId == ownerId && t.DeletedAt == null);

        // 1. Identifiant de la vidéo : exact, et suffisant pour un réimport de la même playlist.
        if (!string.IsNullOrWhiteSpace(identity.ExternalId))
        {
            var externalId = identity.ExternalId.Trim();
            var byExternalId = await owned
                .FirstOrDefaultAsync(
                    t => t.ExternalIds.Any(e => e.Platform == identity.Platform && e.ExternalId == externalId),
                    cancellationToken);

            if (byExternalId is not null)
            {
                return byExternalId;
            }
        }

        // 2. Clé artiste + titre normalisée : reconnaît un morceau déjà présent sous une
        // autre mise en ligne, ou envoyé manuellement.
        var matchKey = MetadataNormalizer.BuildMatchKey(identity.ArtistName, identity.Title);
        if (matchKey is null)
        {
            return null;
        }

        var candidates = await owned
            .Where(t => t.Metadata != null && t.Metadata.MatchKey == matchKey)
            .OrderBy(t => t.CreatedAt)
            .Take(MaxCandidates)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return null;
        }

        // 3. Durée : départage les homonymes et écarte les versions manifestement
        // différentes (live, remix étendu) qui méritent leur propre entrée.
        if (identity.DurationSeconds <= 0)
        {
            return candidates[0];
        }

        return candidates
            .Where(t => MetadataNormalizer.DurationsMatch(t.DurationSeconds, identity.DurationSeconds))
            .OrderBy(t => Math.Abs(t.DurationSeconds - identity.DurationSeconds))
            .FirstOrDefault();
    }

    /// <summary>
    /// Inscrit sur un morceau les éléments d'identification issus de la plateforme.
    ///
    /// Les valeurs déjà connues ne sont pas écrasées, et l'identifiant de plateforme
    /// n'est ajouté qu'une fois : réimporter la même playlist enrichit le morceau sans
    /// jamais le dupliquer. L'appelant reste responsable de l'enregistrement.
    /// </summary>
    public async Task ApplyIdentityAsync(Track track, TrackIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        var metadata = await db.TrackMetadata.FirstOrDefaultAsync(m => m.TrackId == track.Id, cancellationToken);
        if (metadata is null)
        {
            metadata = new TrackMetadata { TrackId = track.Id };
            db.TrackMetadata.Add(metadata);
        }

        metadata.SourcePlatform ??= identity.Platform;
        metadata.MatchKey ??= MetadataNormalizer.BuildMatchKey(
            identity.ArtistName ?? track.ArtistName,
            identity.Title ?? track.Title);

        if (string.IsNullOrWhiteSpace(identity.ExternalId))
        {
            return;
        }

        var exists = await db.TrackExternalIds.AnyAsync(
            e => e.TrackId == track.Id && e.Platform == identity.Platform,
            cancellationToken);

        if (!exists)
        {
            db.TrackExternalIds.Add(new TrackExternalId
            {
                TrackId = track.Id,
                Platform = identity.Platform,
                ExternalId = identity.ExternalId.Trim(),
            });
        }
    }
}
