using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicPlatform.Application.Common;

namespace MusicPlatform.Infrastructure.Media;

/// <summary>Options du téléchargement et de l'exploration réalisés par <c>yt-dlp</c>.</summary>
public sealed class YtDlpOptions
{
    public const string SectionName = "YtDlp";


    /// <summary>
    /// Commande à exécuter. Le paquet Python <c>yt-dlp</c> installe un exécutable du même
    /// nom ; à défaut, renseigner <see cref="PythonPath"/> pour l'appeler comme module.
    /// </summary>
    public string ExecutablePath { get; set; } = "yt-dlp";

    /// <summary>
    /// Interpréteur Python à utiliser lorsque yt-dlp n'est installé que comme module :
    /// la commande devient alors <c>{PythonPath} -m yt_dlp</c>.
    /// </summary>
    public string? PythonPath { get; set; }

    /// <summary>Format audio produit. Le MP3 est retenu par défaut pour sa compatibilité.</summary>
    public string AudioFormat { get; set; } = "mp3";

    /// <summary>
    /// Débit de l'encodage audio. La valeur par défaut garde une vidéo de la durée maximale
    /// autorisée sous la limite de taille appliquée aux fichiers de la plateforme.
    /// </summary>
    public string AudioQuality { get; set; } = "128K";

    /// <summary>Durée maximale acceptée pour une vidéo, en secondes.</summary>
    public int MaxDurationSeconds { get; set; } = 900;

    /// <summary>Délai au-delà duquel un téléchargement est interrompu, en secondes.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Délai au-delà duquel l'énumération d'une playlist ou une recherche est
    /// interrompue, en secondes. Ces opérations ne transfèrent aucun média et doivent
    /// donc aboutir bien plus vite qu'un téléchargement.
    /// </summary>
    public int MetadataTimeoutSeconds { get; set; } = 120;

    /// <summary>Nombre de résultats examinés lors d'une recherche de source audio.</summary>
    public int SearchResults { get; set; } = 5;

    /// <summary>
    /// Dossier parent des téléchargements. Vide, le dossier temporaire du système est utilisé.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Fichier de cookies au format Netscape, nécessaire pour les vidéos soumises à une
    /// vérification d'âge ou de connexion, et pour les serveurs dont l'adresse IP est
    /// bloquée par YouTube. Facultatif.
    /// </summary>
    public string? CookiesFile { get; set; }

    /// <summary>
    /// Clients YouTube essayés successivement, du plus fiable au moins fiable, séparés par
    /// des virgules.
    ///
    /// Aucun client ne convient à toutes les situations : <c>tv</c> accepte les cookies et
    /// n'exige pas de jeton anti-robot, alors que <c>web</c> le réclame sur une adresse de
    /// centre de données mais reste le seul à donner certains formats. Un client est donc
    /// essayé après l'autre tant que YouTube répond par un refus d'authentification.
    /// </summary>
    public string PlayerClients { get; set; } = "tv,web_safari,web";

    /// <summary>
    /// Force l'usage d'IPv4. Les plages IPv6 des hébergeurs sont bloquées bien plus
    /// largement que leurs plages IPv4 : sortir en IPv4 suffit souvent à débloquer un VPS.
    /// </summary>
    public bool ForceIpv4 { get; set; } = true;

    /// <summary>
    /// Proxy HTTP ou SOCKS emprunté pour joindre YouTube, au format accepté par yt-dlp
    /// (<c>http://hôte:port</c>, <c>socks5://hôte:port</c>). Vide, la connexion est directe.
    /// C'est le seul recours lorsque l'adresse du serveur reste bloquée malgré les cookies.
    /// </summary>
    public string? ProxyUrl { get; set; }

    /// <summary>
    /// Pause observée entre deux requêtes adressées à YouTube, en secondes. Une cadence
    /// soutenue depuis une même adresse est le premier signal qui déclenche le blocage.
    /// </summary>
    public int SleepRequestsSeconds { get; set; } = 1;
}

/// <summary>Sortie brute d'une exécution de yt-dlp.</summary>
/// <param name="ExitCode">Code de sortie du processus.</param>
/// <param name="StandardOutput">Flux de sortie standard, où sont écrits les documents JSON.</param>
/// <param name="StandardError">Flux d'erreur, journalisé en cas d'échec.</param>
public readonly record struct YtDlpResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Lancement de <c>yt-dlp</c> comme processus enfant.
///
/// Les arguments sont passés un par un, sans interprétation par un shell : aucune URL
/// issue d'une requête ne peut donc injecter de commande. Le lancement est centralisé
/// ici afin que le téléchargement, l'exploration des playlists et la recherche de source
/// partagent la résolution de la commande, la gestion du délai et celle des erreurs.
/// </summary>
public sealed class YtDlpProcessRunner(IOptions<YtDlpOptions> options, ILogger<YtDlpProcessRunner> logger)
{
    /// <summary>Nom du fichier de cookies préparé, partagé par toutes les invocations.</summary>
    private const string PreparedCookiesFileName = "yt-dlp-cookies-session.txt";

    /// <summary>Première ligne attendue par le parseur de cookies de yt-dlp.</summary>
    private const string NetscapeHeader = "# Netscape HTTP Cookie File";

    /// <summary>
    /// Fragments de message signalant un refus d'authentification plutôt qu'une erreur
    /// définitive. Ils justifient de réessayer avec un autre client YouTube ; toute autre
    /// erreur (vidéo privée, supprimée, lien invalide) se reproduirait à l'identique.
    /// </summary>
    private static readonly string[] AuthenticationFailureMarkers =
    [
        "sign in to confirm",
        "confirm you're not a bot",
        "please sign in",
        "not available on this app",
        "content isn't available",
        "unable to download api page",
        "failed to extract any player response",
        "no video formats found",
        "requested format is not available",
        "cookies are no longer valid",
        "po token",
    ];

    /// <summary>Sérialise la préparation du fichier de cookies entre imports concurrents.</summary>
    private readonly SemaphoreSlim cookiesGate = new(1, 1);

    /// <summary>Options effectives, exposées aux appelants qui composent la ligne de commande.</summary>
    public YtDlpOptions Options { get; } = options.Value;

    /// <summary>
    /// Exécute yt-dlp en essayant successivement chaque client YouTube configuré.
    ///
    /// Le premier client qui aboutit fournit le résultat. Tant que YouTube répond par un
    /// refus d'authentification — le symptôme d'une adresse IP bloquée ou de cookies
    /// ignorés par le client retenu — le client suivant est essayé. Le résultat de la
    /// dernière tentative est retourné lorsque aucun ne fonctionne, afin que l'appelant
    /// journalise le message d'origine de YouTube.
    /// </summary>
    /// <param name="arguments">Arguments communs, hors sélection du client.</param>
    /// <param name="timeoutSeconds">Délai maximal accordé à chaque tentative.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<YtDlpResult> RunAsync(
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var clients = ParsePlayerClients();
        var cookiesPath = await PrepareCookiesAsync(cancellationToken);
        YtDlpResult result = default;

        for (var attempt = 0; attempt < clients.Count; attempt++)
        {
            var client = clients[attempt];
            result = await RunOnceAsync(
                BuildAttemptArguments(arguments, client, cookiesPath),
                timeoutSeconds,
                cancellationToken);

            if (result.ExitCode == 0)
            {
                if (attempt > 0)
                {
                    logger.LogInformation("yt-dlp succeeded with the YouTube '{Client}' client.", client);
                }

                return result;
            }

            if (!IsAuthenticationFailure(result.StandardError) || attempt == clients.Count - 1)
            {
                return result;
            }

            logger.LogWarning(
                "YouTube refused the '{Client}' client ({Error}). Retrying with '{NextClient}'.",
                client,
                Summarize(result.StandardError),
                clients[attempt + 1]);
        }

        return result;
    }

    /// <summary>
    /// Compose les arguments d'une tentative : les arguments communs, le client YouTube
    /// retenu et le fichier de cookies préparé.
    /// </summary>
    private static List<string> BuildAttemptArguments(
        IReadOnlyList<string> arguments,
        string client,
        string? cookiesPath)
    {
        var attemptArguments = new List<string>(arguments.Count + 4)
        {
            "--extractor-args",
            $"youtube:player_client={client}",
        };

        if (cookiesPath is not null)
        {
            attemptArguments.Add("--cookies");
            attemptArguments.Add(cookiesPath);
        }

        attemptArguments.AddRange(arguments);
        return attemptArguments;
    }

    /// <summary>Liste les clients à essayer, en garantissant au moins une tentative.</summary>
    private List<string> ParsePlayerClients()
    {
        var clients = (Options.PlayerClients ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return clients.Count > 0 ? clients : ["default"];
    }

    /// <summary>
    /// Indique si l'échec traduit un refus d'authentification de YouTube, seul cas où
    /// changer de client peut donner un résultat différent.
    /// </summary>
    public static bool IsAuthenticationFailure(string standardError) =>
        !string.IsNullOrWhiteSpace(standardError)
        && AuthenticationFailureMarkers.Any(marker =>
            standardError.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Réduit la sortie d'erreur à une ligne exploitable dans un journal.</summary>
    private static string Summarize(string standardError)
    {
        var line = standardError
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(candidate => candidate.Contains("ERROR", StringComparison.Ordinal))
            ?? standardError.Trim();

        return line.Length > 300 ? line[..300] : line;
    }

    /// <summary>
    /// Lance une fois yt-dlp et retourne ses flux de sortie.
    ///
    /// Les deux flux sont lus en parallèle de l'attente : un processus qui remplirait un
    /// tampon de sortie sans lecteur se bloquerait indéfiniment.
    /// </summary>
    /// <param name="arguments">Arguments, hors résolution de l'exécutable.</param>
    /// <param name="timeoutSeconds">Délai maximal accordé au processus.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <exception cref="ServiceUnavailableException">yt-dlp n'est pas installé.</exception>
    /// <exception cref="UnprocessableException">Le délai imparti a été dépassé.</exception>
    private async Task<YtDlpResult> RunOnceAsync(
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var usesPythonModule = !string.IsNullOrWhiteSpace(Options.PythonPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = usesPythonModule ? Options.PythonPath! : Options.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (usesPythonModule)
        {
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add("yt_dlp");
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            logger.LogError(exception, "yt-dlp could not be started from '{FileName}'.", startInfo.FileName);
            throw new ServiceUnavailableException(
                ErrorCodes.TrackImportUnavailable,
                "Importing from a link is not available on this server: yt-dlp is not installed.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);

            // Seul le délai imparti est traduit en erreur métier : une annulation demandée
            // par l'appelant doit continuer à remonter telle quelle.
            cancellationToken.ThrowIfCancellationRequested();
            throw new UnprocessableException(
                ErrorCodes.TrackImportFailed,
                $"The operation exceeded the time limit of {timeoutSeconds} seconds.");
        }

        return new YtDlpResult(process.ExitCode, await standardOutput, await standardError);
    }

    /// <summary>
    /// Ajoute les options communes à toute invocation. Le client YouTube et le fichier de
    /// cookies ne figurent pas ici : ils dépendent de la tentative et sont ajoutés par
    /// <see cref="RunAsync"/>.
    /// </summary>
    public void AddCommonArguments(List<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        arguments.Add("--no-progress");
        arguments.Add("--quiet");
        arguments.Add("--retries");
        arguments.Add("3");

        // Les avertissements ne sont pas masqués : « the provided YouTube account cookies
        // are no longer valid » n'est qu'un avertissement, et c'est pourtant le seul
        // message qui explique un import refusé sur un serveur bloqué par YouTube.

        if (Options.ForceIpv4)
        {
            arguments.Add("--force-ipv4");
        }

        if (!string.IsNullOrWhiteSpace(Options.ProxyUrl))
        {
            arguments.Add("--proxy");
            arguments.Add(Options.ProxyUrl);
        }

        if (Options.SleepRequestsSeconds > 0)
        {
            arguments.Add("--sleep-requests");
            arguments.Add(Options.SleepRequestsSeconds.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Prépare une copie exploitable du fichier de cookies et retourne son chemin, ou
    /// <c>null</c> si aucun fichier utilisable n'est configuré.
    ///
    /// Trois écueils sont corrigés au passage, chacun suffisant à faire ignorer les
    /// cookies sans message explicite :
    /// le fichier source est souvent monté en lecture seule alors que yt-dlp y réécrit la
    /// session rafraîchie ; un fichier produit sous Windows porte des fins de ligne CRLF
    /// dont le <c>\r</c> se retrouve collé à la valeur du dernier cookie ; et l'en-tête
    /// Netscape, absent des exports manuels, est exigé par le parseur.
    /// </summary>
    private async Task<string?> PrepareCookiesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Options.CookiesFile))
        {
            return null;
        }

        if (!File.Exists(Options.CookiesFile))
        {
            logger.LogWarning(
                "The configured cookies file '{CookiesFile}' does not exist or is not readable. YouTube will be queried anonymously.",
                Options.CookiesFile);
            return null;
        }

        var directory = !string.IsNullOrWhiteSpace(Options.WorkingDirectory) && Directory.Exists(Options.WorkingDirectory)
            ? Options.WorkingDirectory
            : Path.GetTempPath();
        var preparedPath = Path.Combine(directory, PreparedCookiesFileName);

        await cookiesGate.WaitAsync(cancellationToken);

        try
        {
            // La copie n'est refaite que lorsque la source a changé : entre deux imports,
            // le fichier préparé contient la session rafraîchie par yt-dlp, qui vaut mieux
            // que les cookies d'origine.
            if (File.Exists(preparedPath)
                && File.GetLastWriteTimeUtc(preparedPath) >= File.GetLastWriteTimeUtc(Options.CookiesFile))
            {
                return preparedPath;
            }

            var lines = await File.ReadAllLinesAsync(Options.CookiesFile, cancellationToken);
            var normalized = NormalizeCookies(lines);

            if (normalized.Count <= 1)
            {
                logger.LogWarning(
                    "The cookies file '{CookiesFile}' holds no cookie entry. Export it again in Netscape format from a logged-in YouTube session.",
                    Options.CookiesFile);
                return null;
            }

            // Les lignes sont jointes par « \n » explicitement : File.WriteAllLines
            // emploierait Environment.NewLine et réécrirait des CRLF sous Windows, soit
            // précisément le défaut que la normalisation vient de corriger.
            await File.WriteAllTextAsync(
                preparedPath,
                string.Join('\n', normalized) + '\n',
                cancellationToken);

            logger.LogInformation(
                "Prepared {Count} cookies from '{Source}' into the writable file '{Target}'.",
                normalized.Count - 1,
                Options.CookiesFile,
                preparedPath);

            return preparedPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "The cookies file '{CookiesFile}' could not be prepared. Passing it to yt-dlp unchanged.",
                Options.CookiesFile);
            return Options.CookiesFile;
        }
        finally
        {
            cookiesGate.Release();
        }
    }

    /// <summary>
    /// Reconstruit le contenu du fichier de cookies : en-tête Netscape en première ligne,
    /// puis les seules lignes de cookie, débarrassées des retours chariot et des espaces
    /// de fin. <see cref="File.ReadAllLinesAsync(string, CancellationToken)"/> ayant déjà
    /// découpé sur CRLF comme sur LF, la réécriture élimine de fait les fins de ligne
    /// Windows.
    /// </summary>
    private static List<string> NormalizeCookies(IEnumerable<string> lines)
    {
        var normalized = new List<string> { NetscapeHeader };

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();

            // Les commentaires sont écartés, sauf « #HttpOnly_ » qui préfixe un vrai cookie.
            if (trimmed.Length == 0
                || (trimmed.StartsWith('#') && !trimmed.StartsWith("#HttpOnly_", StringComparison.Ordinal)))
            {
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized;
    }

    /// <summary>Interrompt le processus resté actif au-delà du délai imparti.</summary>
    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            logger.LogWarning(exception, "The yt-dlp process could not be terminated.");
        }
    }
}
