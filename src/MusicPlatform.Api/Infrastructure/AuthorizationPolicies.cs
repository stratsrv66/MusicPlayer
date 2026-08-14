using Microsoft.AspNetCore.Authorization;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Api.Infrastructure;

/// <summary>
/// Politiques d'autorisation applicatives.
///
/// Elles couvrent l'accès aux zones d'administration et de modération. Les autorisations
/// portant sur une ressource précise — être propriétaire d'un morceau ou d'une playlist,
/// consulter un profil privé — dépendent de l'entité chargée et sont donc évaluées dans
/// la couche Application, au plus près de la règle métier.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Accès aux fonctions de modération : modérateurs et administrateurs.</summary>
    public const string CanModerateContent = nameof(CanModerateContent);

    /// <summary>Accès aux fonctions d'administration : administrateurs uniquement.</summary>
    public const string CanAccessAdmin = nameof(CanAccessAdmin);

    /// <summary>Enregistre les politiques et exige un utilisateur authentifié par défaut.</summary>
    public static IServiceCollection AddApplicationAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationBuilder()
            .AddPolicy(CanModerateContent, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Moderator), nameof(UserRole.Admin)))
            .AddPolicy(CanAccessAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Admin)));

        return services;
    }
}
