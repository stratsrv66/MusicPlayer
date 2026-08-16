using System.ComponentModel;
using System.Diagnostics;
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
    /// vérification d'âge ou de connexion. Facultatif.
    /// </summary>
    public string? CookiesFile { get; set; }
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
    /// <summary>Options effectives, exposées aux appelants qui composent la ligne de commande.</summary>
    public YtDlpOptions Options { get; } = options.Value;

    /// <summary>
    /// Exécute yt-dlp et retourne ses flux de sortie.
    ///
    /// Les deux flux sont lus en parallèle de l'attente : un processus qui remplirait un
    /// tampon de sortie sans lecteur se bloquerait indéfiniment.
    /// </summary>
    /// <param name="arguments">Arguments, hors résolution de l'exécutable.</param>
    /// <param name="timeoutSeconds">Délai maximal accordé au processus.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <exception cref="ServiceUnavailableException">yt-dlp n'est pas installé.</exception>
    /// <exception cref="UnprocessableException">Le délai imparti a été dépassé.</exception>
    public async Task<YtDlpResult> RunAsync(
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

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

    /// <summary>Ajoute les options communes à toute invocation.</summary>
    public void AddCommonArguments(List<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        arguments.Add("--no-progress");
        arguments.Add("--quiet");
        arguments.Add("--no-warnings");
        arguments.Add("--retries");
        arguments.Add("3");

        // Le client web est le seul à supporter les cookies ET la résolution de
        // signature JS (via Node.js installé dans le conteneur).
        // iOS ignore les cookies ; web+Node.js est la combinaison qui fonctionne.
        arguments.Add("--extractor-args");
        arguments.Add("youtube:player_client=web");

        var cookiesFile = ResolveCookiesPath();
        if (cookiesFile is not null)
        {
            arguments.Add("--cookies");
            arguments.Add(cookiesFile);
        }
    }

    /// <summary>
    /// Résout et prépare un fichier de cookies accessible en écriture pour yt-dlp.
    /// Si le fichier source est monté en lecture seule (ro), une copie temporaire
    /// est créée pour éviter que yt-dlp ne plante lorsqu'il sauvegarde la session.
    /// </summary>
    private string? ResolveCookiesPath()
    {
        if (string.IsNullOrWhiteSpace(Options.CookiesFile))
        {
            return null;
        }

        if (!File.Exists(Options.CookiesFile))
        {
            logger.LogWarning("The configured cookies file '{CookiesFile}' does not exist or is not accessible.", Options.CookiesFile);
            return null;
        }

        try
        {
            var targetDir = !string.IsNullOrWhiteSpace(Options.WorkingDirectory) && Directory.Exists(Options.WorkingDirectory)
                ? Options.WorkingDirectory
                : Path.GetTempPath();

            var workingCookies = Path.Combine(targetDir, "yt-dlp-cookies-session.txt");

            if (!File.Exists(workingCookies) || File.GetLastWriteTimeUtc(Options.CookiesFile) > File.GetLastWriteTimeUtc(workingCookies))
            {
                File.Copy(Options.CookiesFile, workingCookies, overwrite: true);
                logger.LogInformation("Copied cookies from '{Source}' to writable '{Target}'.", Options.CookiesFile, workingCookies);
            }

            return workingCookies;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to create writable copy of cookies file '{CookiesFile}'. Using original.", Options.CookiesFile);
            return Options.CookiesFile;
        }
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
