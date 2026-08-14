using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Infrastructure;
using MusicPlatform.Infrastructure.Security;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

// Nom de la politique CORS appliquée au site web.
const string CorsPolicyName = "web";

var builder = WebApplication.CreateBuilder(args);

// --- Journalisation structurée, avec l'identifiant de trace propagé sur chaque entrée. ---
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// --- Couches applicative et technique. ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// --- Contrôleurs et sérialisation. ---
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Les enums circulent sous forme de chaînes : le contrat reste lisible et stable.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddProblemDetails();
builder.Services.AddApplicationRateLimiting(builder.Configuration);
builder.Services.AddApplicationAuthorization();
AddAuthentication(builder);
AddCors(builder);
AddOpenApi(builder);
AddHealthChecks(builder);
AddObservability(builder);

// Derrière un reverse proxy, les en-têtes transmis portent le schéma et l'IP réelle,
// dont dépendent la génération des liens et le partitionnement du rate limiting.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MusicPlatform API v1");
        options.DocumentTitle = "MusicPlatform API";
    });
}

app.UseCors(CorsPolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
MapHealthChecks(app);

await app.MigrateAndSeedAsync();
await app.RunAsync();

// --- Configuration détaillée, extraite pour garder le flux de démarrage lisible. ---

/// <summary>Configure la validation des jetons d'accès JWT.</summary>
static void AddAuthentication(WebApplicationBuilder builder)
{
    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

    if (jwt.Secret.Length < JwtOptions.MinimumSecretLength)
    {
        throw new InvalidOperationException(
            $"Jwt:Secret must be configured with at least {JwtOptions.MinimumSecretLength} characters.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
}

/// <summary>Autorise uniquement les origines déclarées en configuration.</summary>
static void AddCors(WebApplicationBuilder builder)
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
    {
        if (origins.Length == 0)
        {
            // Sans origine déclarée, aucune requête cross-origin n'est autorisée.
            policy.DisallowCredentials();
            return;
        }

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length")
            .AllowCredentials();
    }));
}

/// <summary>Configure la documentation OpenAPI, avec l'authentification par jeton porteur.</summary>
static void AddOpenApi(WebApplicationBuilder builder)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "MusicPlatform API",
            Version = "v1",
            Description = "API REST de la plateforme musicale : comptes, morceaux, streaming, "
                          + "playlists, social, recherche, statistiques et administration.",
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Jeton d'accès obtenu via /api/v1/auth/login.",
        });

        options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>(),
        });

        var xmlPath = Path.Combine(AppContext.BaseDirectory, "MusicPlatform.Api.xml");
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    });
}

/// <summary>Enregistre les sondes de disponibilité et de bon fonctionnement.</summary>
static void AddHealthChecks(WebApplicationBuilder builder)
{
    var checks = builder.Services.AddHealthChecks();

    var postgres = builder.Configuration.GetConnectionString(MusicPlatform.Infrastructure.DependencyInjection.PostgresConnectionName);
    if (!string.IsNullOrWhiteSpace(postgres))
    {
        checks.AddNpgSql(postgres, name: "postgres", tags: ["ready"]);
    }

    var redis = builder.Configuration.GetConnectionString(MusicPlatform.Infrastructure.DependencyInjection.RedisConnectionName);
    if (!string.IsNullOrWhiteSpace(redis))
    {
        checks.AddRedis(redis, name: "redis", failureStatus: HealthStatus.Degraded, tags: ["ready"]);
    }

    checks.AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);
}

/// <summary>Active les traces et métriques OpenTelemetry lorsqu'un collecteur est configuré.</summary>
static void AddObservability(WebApplicationBuilder builder)
{
    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

    var telemetry = builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("musicplatform-api"));

    telemetry.WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
        }
    });

    telemetry.WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
        }
    });
}

/// <summary>Expose les trois sondes de santé attendues par l'orchestrateur.</summary>
static void MapHealthChecks(WebApplication app)
{
    app.MapHealthChecks("/health");

    // Vivacité : l'application répond, sans interroger ses dépendances.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

    // Disponibilité : PostgreSQL, Redis et le stockage sont vérifiés.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
}

/// <summary>Rend la classe de démarrage visible aux tests d'intégration.</summary>
public partial class Program;
