namespace MusicPlatform.Application.Common;

/// <summary>
/// Construit les motifs <c>LIKE</c> utilisés par la recherche.
/// Les motifs sont mis en minuscules et comparés à une colonne également mise en minuscules,
/// ce qui reste indépendant du fournisseur de base de données.
/// </summary>
public static class SqlPatterns
{
    /// <summary>
    /// Caractère d'échappement des motifs.
    ///
    /// Il doit être passé explicitement à <c>EF.Functions.Like</c> : sans lui, le
    /// fournisseur PostgreSQL génère <c>ESCAPE ''</c>, ce qui désactive tout échappement
    /// et fait échouer les recherches contenant <c>_</c> ou <c>%</c>.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>Caractères ayant une signification particulière dans un motif <c>LIKE</c>.</summary>
    private static readonly char[] Wildcards = ['%', '_', '\\'];

    /// <summary>Motif « contient », avec échappement des jokers saisis par l'utilisateur.</summary>
    public static string Contains(string term) => $"%{Escape(term)}%";

    /// <summary>Motif « commence par ».</summary>
    public static string StartsWith(string term) => $"{Escape(term)}%";

    /// <summary>
    /// Neutralise les jokers d'un terme utilisateur afin qu'une recherche de « 100% »
    /// ne se transforme pas en motif correspondant à tout le catalogue.
    /// </summary>
    private static string Escape(string term)
    {
        var trimmed = term.Trim().ToLowerInvariant();
        if (trimmed.IndexOfAny(Wildcards) < 0)
        {
            return trimmed;
        }

        var builder = new System.Text.StringBuilder(trimmed.Length + 4);
        foreach (var character in trimmed)
        {
            if (Array.IndexOf(Wildcards, character) >= 0)
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
