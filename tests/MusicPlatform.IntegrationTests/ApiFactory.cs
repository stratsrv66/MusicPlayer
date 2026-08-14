using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MusicPlatform.Infrastructure;
using MusicPlatform.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace MusicPlatform.IntegrationTests;

/// <summary>
/// Démarre l'API complète au-dessus d'un PostgreSQL éphémère.
///
/// Testcontainers garantit que les tests s'exécutent contre le vrai moteur de base :
/// les migrations, les contraintes et les requêtes SQL sont donc réellement vérifiées,
/// ce qu'un fournisseur en mémoire ne permettrait pas.
/// Redis n'est pas démarré : son absence est un scénario supporté et testé de fait.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("musicplatform")
        .WithUsername("musicplatform")
        .WithPassword("musicplatform")
        .Build();

    /// <summary>Racine de stockage isolée, supprimée à la fin de la série de tests.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "mp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Variables d'environnement injectées avant la création de l'hôte.
    ///
    /// <c>Program.cs</c> lit sa configuration pendant la construction du
    /// <c>WebApplicationBuilder</c> — notamment pour valider la clé de signature JWT —
    /// c'est-à-dire avant que les sources ajoutées par <c>ConfigureAppConfiguration</c>
    /// ne soient appliquées. Passer par l'environnement est donc le seul moyen fiable
    /// de configurer l'hôte de test sans modifier le code de production.
    /// </summary>
    private void ApplyEnvironment()
    {
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings__Postgres"] = _postgres.GetConnectionString(),
            // Aucune chaîne Redis : l'absence de cache est un scénario supporté.
            ["ConnectionStrings__Redis"] = string.Empty,
            ["Jwt__Secret"] = "integration-tests-signing-key-at-least-32-chars",
            ["Jwt__Issuer"] = "musicplatform-tests",
            ["Jwt__Audience"] = "musicplatform-tests",
            ["Storage__RootPath"] = StorageRoot,
            ["Seed__Admin__Email"] = "admin@test.local",
            ["Seed__Admin__Username"] = "admin",
            ["Seed__Admin__Password"] = "AdminPass123!",

            // Les tests partagent une même adresse d'appel : les quotas de production
            // seraient atteints immédiatement. Ils sont relevés, pas désactivés, et un
            // test dédié vérifie que le limiteur déclenche bien.
            ["RateLimiting__auth__PermitLimit"] = "10000",
            ["RateLimiting__upload__PermitLimit"] = "10000",
            ["RateLimiting__search__PermitLimit"] = "10000",
            ["RateLimiting__write__PermitLimit"] = "10000",
            ["RateLimiting__admin__PermitLimit"] = "10000",
        };

        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Directory.CreateDirectory(StorageRoot);
        ApplyEnvironment();

        // Force la construction de l'hôte et l'application des migrations.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    /// <inheritdoc />
    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();

        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                // Aucune chaîne Redis : le cache doit se comporter comme absent.
                ["ConnectionStrings:Redis"] = string.Empty,
                ["Jwt:Secret"] = "integration-tests-signing-key-at-least-32-chars",
                ["Jwt:Issuer"] = "musicplatform-tests",
                ["Jwt:Audience"] = "musicplatform-tests",
                ["Storage:RootPath"] = StorageRoot,
                ["Seed:Admin:Email"] = "admin@test.local",
                ["Seed:Admin:Username"] = "admin",
                ["Seed:Admin:Password"] = "AdminPass123!",
            });
        });
    }

    /// <summary>Crée un client HTTP qui ne suit pas les redirections, pour observer les statuts bruts.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Inscrit un utilisateur et retourne un client authentifié en son nom.</summary>
    public async Task<AuthenticatedClient> RegisterAsync(string username, string? password = null)
    {
        var client = CreateApiClient();
        var email = $"{username}@test.local";
        var effectivePassword = password ?? "TestPass123!";

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, username, password = effectivePassword });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>()
            ?? throw new InvalidOperationException("The registration response is empty.");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        return new AuthenticatedClient(client, payload.User.Id, username, payload.AccessToken, payload.RefreshToken);
    }

    /// <summary>Retourne un client authentifié en tant qu'administrateur initial.</summary>
    public async Task<AuthenticatedClient> LoginAdminAsync()
    {
        var client = CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "admin@test.local", password = "AdminPass123!" });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>()
            ?? throw new InvalidOperationException("The login response is empty.");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        return new AuthenticatedClient(client, payload.User.Id, "admin", payload.AccessToken, payload.RefreshToken);
    }

    /// <summary>Forme minimale de la réponse d'authentification utilisée par les tests.</summary>
    private sealed record AuthPayload(string AccessToken, string RefreshToken, int ExpiresIn, UserPayload User);

    private sealed record UserPayload(Guid Id, string Username);
}

/// <summary>Client HTTP authentifié, accompagné de l'identité de son utilisateur.</summary>
public sealed record AuthenticatedClient(HttpClient Client, Guid UserId, string Username, string AccessToken, string RefreshToken);

/// <summary>Regroupe les tests d'intégration afin de ne démarrer qu'un seul conteneur.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
