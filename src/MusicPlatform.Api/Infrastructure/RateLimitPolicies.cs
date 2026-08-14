using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using MusicPlatform.Application.Common;

namespace MusicPlatform.Api.Infrastructure;

/// <summary>Noms des politiques de limitation de débit appliquées aux endpoints sensibles.</summary>
public static class RateLimitPolicies
{
    /// <summary>Connexion et inscription : protège contre le bourrage d'identifiants.</summary>
    public const string Authentication = "auth";

    /// <summary>Upload de fichiers : limite le coût disque et CPU par utilisateur.</summary>
    public const string Upload = "upload";

    /// <summary>Recherche : limite les requêtes coûteuses déclenchées à la frappe.</summary>
    public const string Search = "search";

    /// <summary>Écriture de contenu social (commentaires, likes, signalements).</summary>
    public const string Write = "write";

    /// <summary>Endpoints d'administration.</summary>
    public const string Admin = "admin";
}

/// <summary>Configuration de la limitation de débit.</summary>
public static class RateLimitingSetup
{
    /// <summary>Section de configuration permettant d'ajuster les quotas par déploiement.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Quotas par défaut, exprimés en requêtes autorisées par fenêtre.</summary>
    private static readonly Dictionary<string, (int PermitLimit, int WindowMinutes)> Defaults = new()
    {
        [RateLimitPolicies.Authentication] = (10, 1),
        [RateLimitPolicies.Upload] = (20, 10),
        [RateLimitPolicies.Search] = (120, 1),
        [RateLimitPolicies.Write] = (60, 1),
        [RateLimitPolicies.Admin] = (200, 1),
    };

    /// <summary>
    /// Enregistre les politiques. Le partitionnement se fait par utilisateur authentifié,
    /// ou à défaut par adresse IP, afin qu'un client ne pénalise pas les autres.
    ///
    /// L'implémentation retenue est celle d'ASP.NET Core, en mémoire : elle reste correcte
    /// et disponible même lorsque Redis est injoignable, conformément aux contraintes du projet.
    ///
    /// Les quotas sont surchargeables par configuration (<c>RateLimiting:auth:PermitLimit</c>),
    /// ce qui permet de les adapter à un déploiement sans modifier le code.
    /// </summary>
    public static IServiceCollection AddApplicationRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            foreach (var (policy, defaults) in Defaults)
            {
                var policySection = section.GetSection(policy);
                var permitLimit = policySection.GetValue("PermitLimit", defaults.PermitLimit);
                var windowMinutes = policySection.GetValue("WindowMinutes", defaults.WindowMinutes);

                AddFixedWindow(options, policy, permitLimit, windowMinutes);
            }

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "https://musicplatform.dev/errors/rate-limit-exceeded",
                        title = "Too many requests",
                        status = StatusCodes.Status429TooManyRequests,
                        code = ErrorCodes.RateLimitExceeded,
                        detail = "You have sent too many requests. Please slow down and try again later.",
                        traceId = context.HttpContext.TraceIdentifier,
                    },
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>Ajoute une politique à fenêtre fixe partitionnée par appelant.</summary>
    private static void AddFixedWindow(RateLimiterOptions options, string policyName, int permitLimit, int windowMinutes) =>
        options.AddPolicy(policyName, context => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context, policyName),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0,
            }));

    /// <summary>Identifie l'appelant : identifiant utilisateur si connu, sinon adresse IP.</summary>
    private static string PartitionKey(HttpContext context, string policyName)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            return $"{policyName}:u:{userId}";
        }

        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"{policyName}:ip:{address}";
    }
}
