using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Domain.Entities;

/// <summary>
/// Référence vers un fichier binaire géré par <c>IFileStorage</c> (avatar, pochette de playlist,
/// pochette d'album). Le chemin est interne et n'est jamais exposé au client.
/// </summary>
public sealed class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StoragePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Album regroupant plusieurs morceaux.</summary>
public sealed class Album
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public Guid? CoverId { get; set; }
    public StoredFile? Cover { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}

/// <summary>Genre musical, administré par les administrateurs.</summary>
public sealed class Genre
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}

/// <summary>
/// Tag libre associé aux morceaux. Le préfixe <c>#</c> est une convention d'affichage :
/// seul le nom normalisé est stocké.
/// </summary>
public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TrackTag> TrackTags { get; set; } = new List<TrackTag>();

    /// <summary>
    /// Normalise un libellé saisi par l'utilisateur en slug de tag : minuscules, sans
    /// <c>#</c>, sans accents ni caractères spéciaux, espaces convertis en tirets.
    /// Retourne une chaîne vide si l'entrée ne contient aucun caractère exploitable.
    /// </summary>
    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var trimmed = raw.Trim().TrimStart('#').ToLowerInvariant();
        var normalized = trimmed.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        var lastWasSeparator = false;

        for (var i = 0; i < normalized.Length; i++)
        {
            var current = normalized[i];
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(current);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(current))
            {
                builder.Append(current);
                lastWasSeparator = false;
                continue;
            }

            // Les séparateurs consécutifs sont réduits à un seul tiret.
            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}

/// <summary>Association many-to-many entre un morceau et un tag.</summary>
public sealed class TrackTag
{
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

/// <summary>Journal des actions sensibles réalisées par les modérateurs et administrateurs.</summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }

    /// <summary>Contexte additionnel sérialisé en JSON. Ne doit contenir aucun secret.</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Signalement d'un contenu par un utilisateur.</summary>
public sealed class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReporterId { get; set; }
    public User Reporter { get; set; } = null!;
    public ReportTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public ReportReason Reason { get; set; }
    public string? Description { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Pending;

    /// <summary>Justification saisie par le modérateur lors du traitement.</summary>
    public string? ResolutionNote { get; set; }

    public Guid? ReviewedBy { get; set; }
    public User? Reviewer { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
