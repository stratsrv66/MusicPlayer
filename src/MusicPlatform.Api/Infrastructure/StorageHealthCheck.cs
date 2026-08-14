using Microsoft.Extensions.Diagnostics.HealthChecks;
using MusicPlatform.Application.Abstractions;

namespace MusicPlatform.Api.Infrastructure;

/// <summary>
/// Vérifie que le stockage de fichiers est accessible en écriture.
/// Sans lui, ni l'upload ni le streaming ne peuvent fonctionner : l'instance ne doit
/// donc pas être déclarée prête.
/// </summary>
public sealed class StorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var healthy = await storage.IsHealthyAsync(cancellationToken);

        return healthy
            ? HealthCheckResult.Healthy("File storage is writable.")
            : HealthCheckResult.Unhealthy("File storage is not writable.");
    }
}
