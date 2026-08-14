using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using StackExchange.Redis;

namespace MusicPlatform.Infrastructure.Caching;

/// <summary>
/// Porte la connexion Redis, qui peut être absente lorsque le cache n'est pas configuré
/// ou que le serveur est injoignable. Le conteneur d'injection ne sait pas enregistrer
/// un service dont le type est nullable : ce porteur explicite lève l'ambiguïté.
/// </summary>
/// <param name="Multiplexer">Connexion partagée, ou <c>null</c> si le cache est indisponible.</param>
public sealed record RedisConnection(IConnectionMultiplexer? Multiplexer);

/// <summary>
/// Cache Redis. Redis n'est jamais la source de vérité : toute indisponibilité est
/// journalisée puis absorbée, l'appelant se rabattant sur PostgreSQL.
/// </summary>
public sealed class RedisCacheService(RedisConnection redis, ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var database = TryGetDatabase();
        if (database is null)
        {
            return null;
        }

        try
        {
            var value = await database.StringGetAsync(key);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>((string)value!, SerializerOptions);
        }
        catch (Exception exception) when (exception is RedisException or JsonException)
        {
            logger.LogWarning(exception, "Cache read failed for key {Key}.", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class
    {
        var database = TryGetDatabase();
        if (database is null)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            await database.StringSetAsync(key, payload, ttl);
        }
        catch (Exception exception) when (exception is RedisException or JsonException)
        {
            logger.LogWarning(exception, "Cache write failed for key {Key}.", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var database = TryGetDatabase();
        if (database is null)
        {
            return;
        }

        try
        {
            await database.KeyDeleteAsync(key);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Cache eviction failed for key {Key}.", key);
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryMarkAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var database = TryGetDatabase();
        if (database is null)
        {
            // Sans cache, on laisse passer : la déduplication de secours est faite en base.
            return true;
        }

        try
        {
            return await database.StringSetAsync(key, "1", ttl, when: When.NotExists);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Cache marker failed for key {Key}.", key);
            return true;
        }
    }

    /// <summary>Retourne la base Redis si la connexion est disponible, sinon <c>null</c>.</summary>
    private IDatabase? TryGetDatabase()
    {
        if (redis.Multiplexer is not { IsConnected: true } multiplexer)
        {
            return null;
        }

        try
        {
            return multiplexer.GetDatabase();
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis connection is unavailable.");
            return null;
        }
    }
}
