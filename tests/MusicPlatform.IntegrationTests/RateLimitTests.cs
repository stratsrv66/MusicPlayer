using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace MusicPlatform.IntegrationTests;

/// <summary>
/// Hôte configuré avec des quotas volontairement bas, afin de vérifier que la limitation
/// de débit protège réellement les endpoints sensibles.
///
/// La suite est exécutée sans parallélisme (voir <c>AssemblyInfo.cs</c>) : les quotas
/// passent par des variables d'environnement, qui sont globales au processus.
/// </summary>
public sealed class ThrottledApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Nombre de tentatives d'authentification autorisées par minute dans ce test.</summary>
    public const int AuthPermitLimit = 3;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("musicplatform")
        .WithUsername("musicplatform")
        .WithPassword("musicplatform")
        .Build();

    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "mp-rl", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Directory.CreateDirectory(_storageRoot);

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", string.Empty);
        Environment.SetEnvironmentVariable("Jwt__Secret", "rate-limit-tests-signing-key-at-least-32-chars");
        Environment.SetEnvironmentVariable("Storage__RootPath", _storageRoot);
        Environment.SetEnvironmentVariable("Seed__Admin__Email", string.Empty);
        Environment.SetEnvironmentVariable("RateLimiting__auth__PermitLimit", AuthPermitLimit.ToString());
        Environment.SetEnvironmentVariable("RateLimiting__auth__WindowMinutes", "5");

        _ = Services;
    }

    /// <inheritdoc />
    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();

        // Les quotas sont remis à un niveau permissif pour les autres suites du processus.
        Environment.SetEnvironmentVariable("RateLimiting__auth__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__auth__WindowMinutes", null);

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
    }
}

/// <summary>Vérifie que la limitation de débit répond bien en Problem Details 429.</summary>
public sealed class RateLimitTests : IClassFixture<ThrottledApiFactory>
{
    private readonly ThrottledApiFactory _factory;

    public RateLimitTests(ThrottledApiFactory factory) => _factory = factory;

    [Fact]
    public async Task RepeatedLoginAttemptsAreThrottledWithAProblemDetailsResponse()
    {
        var client = _factory.CreateClient();
        var attempts = new List<HttpResponseMessage>();

        // Une tentative de plus que le quota : la dernière doit être refusée.
        for (var i = 0; i <= ThrottledApiFactory.AuthPermitLimit; i++)
        {
            attempts.Add(await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email = $"nobody{i}@test.local", password = "WrongPass123!" }));
        }

        Assert.All(
            attempts.Take(ThrottledApiFactory.AuthPermitLimit),
            response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));

        var throttled = attempts[^1];
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        var problem = await throttled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RATE_LIMIT_EXCEEDED", problem.GetProperty("code").GetString());
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task PublicReadEndpointsAreNotThrottledByTheAuthenticationPolicy()
    {
        var client = _factory.CreateClient();

        // Le catalogue reste consultable même après saturation du quota d'authentification.
        for (var i = 0; i < ThrottledApiFactory.AuthPermitLimit + 3; i++)
        {
            var response = await client.GetAsync("/api/v1/genres");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
