using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Infrastructure.Caching;
using MusicPlatform.Infrastructure.Jobs;
using MusicPlatform.Infrastructure.Media;
using MusicPlatform.Infrastructure.Persistence;
using MusicPlatform.Infrastructure.Security;
using StackExchange.Redis;

namespace MusicPlatform.Infrastructure;

/// <summary>Enregistrement des implémentations techniques dans le conteneur.</summary>
public static class DependencyInjection
{
    /// <summary>Nom de la chaîne de connexion PostgreSQL.</summary>
    public const string PostgresConnectionName = "Postgres";

    /// <summary>Nom de la chaîne de connexion Redis.</summary>
    public const string RedisConnectionName = "Redis";

    /// <summary>
    /// Enregistre la persistance, le stockage, la sécurité, le cache et les traitements
    /// de fond. Redis est optionnel : son absence dégrade les performances sans casser
    /// aucune fonctionnalité.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{PostgresConnectionName} is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<DatabaseSeeder>();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
        services.AddScoped<IAudioMetadataExtractor, TagLibAudioMetadataExtractor>();

        AddRedis(services, configuration);

        services.AddSingleton<ChannelBackgroundJobQueue>();
        services.AddSingleton<IBackgroundJobQueue>(provider => provider.GetRequiredService<ChannelBackgroundJobQueue>());
        services.AddHostedService<BackgroundJobRunner>();
        services.AddHostedService<StalledJobRecoveryService>();
        services.AddHostedService<MaintenanceService>();

        return services;
    }

    /// <summary>
    /// Enregistre la connexion Redis si elle est configurée. L'échec de connexion au
    /// démarrage n'est pas fatal : le cache se comporte alors comme absent.
    /// </summary>
    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString(RedisConnectionName);

        services.AddSingleton(provider =>
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                return new RedisConnection(null);
            }

            var logger = provider.GetRequiredService<ILogger<RedisCacheService>>();

            try
            {
                var options = ConfigurationOptions.Parse(redisConnectionString);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = 3;
                return new RedisConnection(ConnectionMultiplexer.Connect(options));
            }
            catch (RedisConnectionException exception)
            {
                logger.LogWarning(exception, "Redis is unreachable; the application will run without cache.");
                return new RedisConnection(null);
            }
        });

        services.AddSingleton<ICacheService, RedisCacheService>();
    }

    /// <summary>
    /// Applique les migrations en attente puis insère les données de référence.
    /// Appelé au démarrage afin qu'un environnement neuf soit immédiatement utilisable.
    /// </summary>
    public static async Task MigrateAndSeedAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
    }
}
