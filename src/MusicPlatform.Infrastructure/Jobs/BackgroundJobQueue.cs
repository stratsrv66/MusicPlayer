using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Features.Account;
using MusicPlatform.Application.Features.Tracks;

namespace MusicPlatform.Infrastructure.Jobs;

/// <summary>Nature du travail à exécuter en arrière-plan.</summary>
public enum BackgroundJobKind
{
    TrackProcessing = 0,
    UserExport = 1,
}

/// <summary>Travail placé dans la file d'exécution différée.</summary>
/// <param name="Kind">Type de traitement à déclencher.</param>
/// <param name="TargetId">Identifiant de l'opération d'upload ou de l'export concerné.</param>
public readonly record struct BackgroundJob(BackgroundJobKind Kind, Guid TargetId);

/// <summary>
/// File de travaux en mémoire, adossée à un <c>Channel</c> borné.
///
/// Ce choix évite d'introduire un courtier de messages dans le MVP tout en respectant
/// l'abstraction <see cref="IBackgroundJobQueue"/> : une implémentation distribuée pourra
/// la remplacer sans modifier les cas d'utilisation. Les travaux perdus en cas d'arrêt
/// brutal sont rattrapés au démarrage par <see cref="StalledJobRecoveryService"/>.
/// </summary>
public sealed class ChannelBackgroundJobQueue : IBackgroundJobQueue
{
    /// <summary>Capacité de la file : au-delà, la mise en file attend qu'une place se libère.</summary>
    private const int Capacity = 1000;

    private readonly Channel<BackgroundJob> _channel = Channel.CreateBounded<BackgroundJob>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait });

    /// <summary>Permet au service d'exécution de consommer la file.</summary>
    public ChannelReader<BackgroundJob> Reader => _channel.Reader;

    /// <inheritdoc />
    public ValueTask EnqueueTrackProcessingAsync(Guid uploadOperationId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(new BackgroundJob(BackgroundJobKind.TrackProcessing, uploadOperationId), cancellationToken);

    /// <inheritdoc />
    public ValueTask EnqueueUserExportAsync(Guid exportId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(new BackgroundJob(BackgroundJobKind.UserExport, exportId), cancellationToken);
}

/// <summary>
/// Service hébergé consommant la file de travaux. Chaque travail est exécuté dans sa
/// propre portée d'injection, avec son propre <c>DbContext</c>, et une exception ne fait
/// jamais tomber la boucle de traitement.
/// </summary>
public sealed class BackgroundJobRunner(
    ChannelBackgroundJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobRunner> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background job runner started.");

        // La boucle se termine lorsque le jeton d'arrêt est déclenché : sa condition de
        // sortie est explicite et portée par l'hôte, conformément aux règles de codage.
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ExecuteJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job {Kind} for {TargetId} failed.", job.Kind, job.TargetId);
            }
        }

        logger.LogInformation("Background job runner stopped.");
    }

    /// <summary>Exécute un travail dans une portée dédiée.</summary>
    private async Task ExecuteJobAsync(BackgroundJob job, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        switch (job.Kind)
        {
            case BackgroundJobKind.TrackProcessing:
                await scope.ServiceProvider
                    .GetRequiredService<TrackProcessingService>()
                    .ProcessAsync(job.TargetId, cancellationToken);
                break;

            case BackgroundJobKind.UserExport:
                await scope.ServiceProvider
                    .GetRequiredService<UserExportGenerator>()
                    .GenerateAsync(job.TargetId, cancellationToken);
                break;

            default:
                logger.LogWarning("Unknown background job kind {Kind}.", job.Kind);
                break;
        }
    }
}
