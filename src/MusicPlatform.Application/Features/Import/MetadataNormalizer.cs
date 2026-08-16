using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicPlatform.Application.Features.Import;

/// <summary>
/// Normalisation des métadonnées relevées sur les plateformes externes.
///
/// Un même enregistrement n'est presque jamais intitulé de façon identique d'une
/// plateforme à l'autre : « Artiste - Titre (Official Video) » sur YouTube, « Titre -
/// Remastered 2011 » sur Spotify. La normalisation ramène ces variantes à une forme
/// commune afin que le rapprochement par artiste et titre reste exploitable lorsque
/// l'ISRC fait défaut.
/// </summary>
public static partial class MetadataNormalizer
{
    /// <summary>Écart de durée toléré entre deux versions d'un même enregistrement, en secondes.</summary>
    public const int DurationToleranceSeconds = 5;

    /// <summary>
    /// Mots signalant un habillage éditorial plutôt qu'une différence d'enregistrement.
    /// Seuls les groupes entre parenthèses ou crochets qui en contiennent sont retirés,
    /// afin de préserver les mentions signifiantes comme « (Live at Wembley) ».
    /// </summary>
    private static readonly string[] DecorativeKeywords =
    [
        "official", "video", "audio", "lyric", "lyrics", "visualizer", "visualiser",
        "hd", "hq", "4k", "mv", "clip", "remaster", "remastered", "explicit",
        "monstercat release", "free download", "npm",
    ];

    /// <summary>
    /// Réduit un libellé à sa forme comparable : sans habillage, sans mention
    /// d'invité, sans accent, sans ponctuation, en minuscules.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value;
        text = StripDecoratedGroups(text);
        text = FeaturingPattern().Replace(text, " ");
        text = RemoveDiacritics(text);
        text = NonAlphanumericPattern().Replace(text, " ");
        text = WhitespacePattern().Replace(text, " ");

        return text.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Retient l'artiste principal d'un crédit. Les plateformes listent les artistes
    /// associés de façon variable : « Daft Punk » d'un côté, « Daft Punk, Pharrell
    /// Williams » de l'autre. Le premier nom est le seul repère stable.
    /// </summary>
    public static string PrimaryArtist(string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return string.Empty;
        }

        var separators = new[] { ",", ";", " & ", " x ", " X ", " feat", " ft.", " vs ", " with " };
        var text = artistName;

        foreach (var separator in separators)
        {
            var index = text.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                text = text[..index];
            }
        }

        return text.Trim();
    }

    /// <summary>
    /// Construit la clé de rapprochement « artiste|titre » persistée sur le morceau.
    /// Retourne <c>null</c> lorsque les deux composantes sont vides : une clé vide
    /// rapprocherait à tort tous les morceaux sans métadonnées.
    /// </summary>
    public static string? BuildMatchKey(string? artistName, string? title)
    {
        var artist = Normalize(PrimaryArtist(artistName));
        var normalizedTitle = Normalize(title);

        if (artist.Length == 0 && normalizedTitle.Length == 0)
        {
            return null;
        }

        return $"{artist}|{normalizedTitle}";
    }

    /// <summary>
    /// Sépare un titre de vidéo de la forme « Artiste - Titre ».
    /// Les plateformes vidéo ne distinguent pas les deux champs ; ce découpage améliore
    /// nettement le rapprochement d'un import YouTube avec le reste de la bibliothèque.
    /// </summary>
    /// <param name="videoTitle">Titre complet de la vidéo.</param>
    /// <param name="uploader">Chaîne ayant publié la vidéo, utilisée en repli.</param>
    public static (string Artist, string Title) SplitVideoTitle(string videoTitle, string? uploader)
    {
        if (string.IsNullOrWhiteSpace(videoTitle))
        {
            return (uploader ?? string.Empty, string.Empty);
        }

        var separators = new[] { " - ", " – ", " — ", " | " };

        foreach (var separator in separators)
        {
            var index = videoTitle.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0)
            {
                continue;
            }

            var artist = videoTitle[..index].Trim();
            var title = videoTitle[(index + separator.Length)..].Trim();

            if (artist.Length > 0 && title.Length > 0)
            {
                return (artist, title);
            }
        }

        // Sans séparateur exploitable, la chaîne d'origine fait office d'artiste.
        return (uploader?.Trim() ?? string.Empty, videoTitle.Trim());
    }

    /// <summary>Indique si deux durées peuvent désigner le même enregistrement.</summary>
    public static bool DurationsMatch(int left, int right) =>
        left > 0 && right > 0 && Math.Abs(left - right) <= DurationToleranceSeconds;

    /// <summary>Retire les groupes parenthésés qui ne portent qu'un habillage éditorial.</summary>
    private static string StripDecoratedGroups(string text) =>
        BracketedGroupPattern().Replace(text, match =>
        {
            var content = match.Value.ToLowerInvariant();
            return DecorativeKeywords.Any(keyword => content.Contains(keyword, StringComparison.Ordinal))
                ? " "
                : match.Value;
        });

    /// <summary>
    /// Supprime les signes diacritiques en décomposant les caractères composés.
    /// La globalisation invariante doit rester désactivée pour que cette étape opère.
    /// </summary>
    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Groupes entre parenthèses ou crochets.</summary>
    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex BracketedGroupPattern();

    /// <summary>Mention d'artiste invité et tout ce qui la suit.</summary>
    [GeneratedRegex(@"\s(feat\.?|ft\.?|featuring|avec)\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturingPattern();

    /// <summary>Tout caractère qui n'est ni une lettre, ni un chiffre, ni un espace.</summary>
    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]")]
    private static partial Regex NonAlphanumericPattern();

    /// <summary>Suites d'espaces.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
