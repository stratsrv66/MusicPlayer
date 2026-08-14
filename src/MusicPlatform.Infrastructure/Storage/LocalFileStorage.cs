using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicPlatform.Application.Abstractions;

namespace MusicPlatform.Infrastructure.Storage;

/// <summary>Options de configuration du stockage local.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Racine physique du stockage. Doit exister ou pouvoir être créée au démarrage.</summary>
    public string RootPath { get; set; } = "storage";
}

/// <summary>
/// Implémentation de <see cref="IFileStorage"/> adossée au disque local.
///
/// Tous les chemins reçus sont relatifs et normalisés : la résolution vérifie que le
/// chemin final reste sous la racine configurée, ce qui empêche toute traversée de
/// répertoire à partir d'une valeur issue d'une requête.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    /// <summary>Taille du tampon de copie, alignée sur la valeur par défaut de .NET.</summary>
    private const int CopyBufferSize = 81920;

    private readonly string _root;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IOptions<StorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public async Task<FileWriteResult> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Écriture d'abord dans un fichier voisin : un échec ne laisse jamais un fichier
        // partiel à l'emplacement définitif.
        var stagingPath = fullPath + ".partial";

        try
        {
            long size;
            byte[] hash;

            await using (var destination = new FileStream(
                stagingPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
            using (var sha = SHA256.Create())
            await using (var hashing = new CryptoStream(destination, sha, CryptoStreamMode.Write, leaveOpen: true))
            {
                await content.CopyToAsync(hashing, CopyBufferSize, cancellationToken);
                await hashing.FlushFinalBlockAsync(cancellationToken);
                size = destination.Length;
                hash = sha.Hash ?? [];
            }

            File.Move(stagingPath, fullPath, overwrite: true);
            return new FileWriteResult(relativePath, size, Convert.ToHexString(hash).ToLowerInvariant());
        }
        catch
        {
            TryDelete(stagingPath);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Resolve(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested file does not exist in storage.", relativePath);
        }

        // FileStream positionnable : indispensable au traitement des requêtes HTTP Range.
        Stream stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWriteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        Stream stream = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<FileStat?> StatAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = new FileInfo(Resolve(relativePath));
        return Task.FromResult<FileStat?>(info.Exists ? new FileStat(info.Length, info.LastWriteTimeUtc) : null);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Resolve(relativePath);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DeleteDirectoryAsync(string relativePrefix, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Resolve(relativePrefix);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var probePath = Path.Combine(_root, ".health");

        try
        {
            await File.WriteAllTextAsync(probePath, DateTime.UtcNow.ToString("O"), cancellationToken);
            File.Delete(probePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Storage health probe failed at {Root}.", _root);
            return false;
        }
    }

    /// <summary>
    /// Convertit un chemin logique en chemin physique et refuse tout chemin qui sortirait
    /// de la racine de stockage.
    /// </summary>
    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A storage path is required.", nameof(relativePath));
        }

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var candidate = Path.GetFullPath(Path.Combine(_root, normalized));

        if (!candidate.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The resolved storage path escapes the storage root.");
        }

        return candidate;
    }

    /// <summary>Supprime un fichier de travail en ignorant son absence.</summary>
    private void TryDelete(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not remove staging file {Path}.", fullPath);
        }
    }
}
