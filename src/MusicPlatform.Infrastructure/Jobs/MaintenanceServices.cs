using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Domain.Enums;
using MusicPlatform.Infrastructure.Persistence;

namespace MusicPlatform.Infrastructure.Jobs;

/// <summary>
/// Au démarrage, remet en file les traitements laissés en suspens par un arrêt brutal
/// et marque en échec ceux qui sont trop anciens pour être repris.
/// </summary>
public sealed class StalledJobRecoveryService(
    IServiceScopeFactory scopeFactory,
    ILogger<StalledJobRecoveryService> logger) : IHostedService
{
    /// <summary>Au-delà de ce délai, un traitement en cours est considéré comme perdu.</summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var cutoff = now - StaleThreshold;

        var pendingUploads = await db.UploadOperations
            .Where(o => o.Status == UploadOperationStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var operation in pendingUploads)
        {
            if (operation.UpdatedAt >= cutoff)
            {
                await queue.EnqueueTrackProcessingAsync(operation.Id, cancellationToken);
                continue;
            }

            await FailStaleUploadAsync(db, storage, operation, now, cancellationToken);
        }

        var pendingExports = await db.UserExports
            .Where(e => e.Status == UserExportStatus.Pending || e.Status == UserExportStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var export in pendingExports)
        {
            // Un export interrompu est simplement rejoué depuis l'état PENDING.
            export.Status = UserExportStatus.Pending;
            await queue.EnqueueUserExportAsync(export.Id, cancellationToken);
        }

        // Un import de playlist conserve son inventaire en base : le remettre en file
        // suffit, le runner reprenant les morceaux restés en attente.
        var pendingImports = await db.PlaylistImports
            .Where(i => i.Status == PlaylistImportStatus.Pending || i.Status == PlaylistImportStatus.Running)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        foreach (var importId in pendingImports)
        {
            await queue.EnqueuePlaylistImportAsync(importId, cancellationToken);
        }

        if (pendingUploads.Count > 0 || pendingExports.Count > 0 || pendingImports.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Recovered {Uploads} upload(s), {Exports} export(s) and {Imports} playlist import(s) after restart.",
                pendingUploads.Count,
                pendingExports.Count,
                pendingImports.Count);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Marque un upload abandonné comme échoué et supprime son fichier temporaire.</summary>
    private static async Task FailStaleUploadAsync(
        AppDbContext db,
        IFileStorage storage,
        Domain.Entities.UploadOperation operation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (operation.TemporaryPath is not null)
        {
            await storage.DeleteAsync(operation.TemporaryPath, cancellationToken);
            operation.TemporaryPath = null;
        }

        operation.Status = UploadOperationStatus.Failed;
        operation.FailureReason = "Processing was interrupted and could not be resumed.";
        operation.CompletedAt = now;
        operation.UpdatedAt = now;

        if (operation.TrackId is { } trackId)
        {
            var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == trackId, cancellationToken);
            track?.MarkFailed(operation.FailureReason, now);
        }
    }
}

/// <summary>
/// Tâche périodique de maintenance : expiration des archives d'export, suppression des
/// fichiers temporaires abandonnés et purge des refresh tokens obsolètes.
/// </summary>
public sealed class MaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<MaintenanceService> logger) : BackgroundService
{
    /// <summary>Intervalle entre deux passes de maintenance.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // Une première passe immédiate, puis une passe à chaque tick jusqu'à l'arrêt de l'hôte.
        await RunSafelyAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSafelyAsync(stoppingToken);
        }
    }

    /// <summary>Exécute une passe en isolant les erreurs pour ne jamais arrêter la boucle.</summary>
    private async Task RunSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Maintenance pass failed.");
        }
    }

    /// <summary>Applique les trois nettoyages de maintenance.</summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var expired = await db.UserExports
            .Where(e => e.Status == UserExportStatus.Ready && e.ExpiresAt != null && e.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var export in expired)
        {
            if (export.StoragePath is not null)
            {
                await storage.DeleteAsync(export.StoragePath, cancellationToken);
            }

            export.Status = UserExportStatus.Expired;
            export.StoragePath = null;
        }

        var abandonedUploads = await db.UploadOperations
            .Where(o => o.TemporaryPath != null
                        && (o.Status == UploadOperationStatus.Failed || o.Status == UploadOperationStatus.Cancelled))
            .ToListAsync(cancellationToken);

        foreach (var operation in abandonedUploads)
        {
            await storage.DeleteAsync(operation.TemporaryPath!, cancellationToken);
            operation.TemporaryPath = null;
        }

        var removedTokens = await db.RefreshTokens
            .Where(t => t.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Maintenance completed: {Exports} export(s) expired, {Uploads} temporary file(s) removed, {Tokens} token(s) purged.",
            expired.Count,
            abandonedUploads.Count,
            removedTokens);
    }
}
