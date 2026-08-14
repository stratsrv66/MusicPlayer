using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Abstractions;

/// <summary>Fournit l'heure courante, injectable afin de rendre les règles temporelles testables.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Identité de l'appelant de la requête HTTP courante.</summary>
public interface ICurrentUser
{
    /// <summary>Identifiant de l'utilisateur authentifié, ou <c>null</c> pour un appel anonyme.</summary>
    Guid? UserId { get; }

    /// <summary>Rôle effectif de l'appelant. Vaut <see cref="UserRole.User"/> pour un anonyme.</summary>
    UserRole Role { get; }

    bool IsAuthenticated { get; }

    /// <summary>Identifiant utilisateur ou exception si l'appel n'est pas authentifié.</summary>
    Guid RequireUserId();
}

/// <summary>Hachage et vérification des mots de passe.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Vérifie un mot de passe et indique si le hash doit être recalculé.</summary>
    (bool Succeeded, bool NeedsRehash) Verify(string hash, string password);
}

/// <summary>Jeton d'accès émis pour un utilisateur.</summary>
/// <param name="AccessToken">JWT signé à durée courte.</param>
/// <param name="RefreshToken">Jeton opaque permettant le renouvellement.</param>
/// <param name="ExpiresInSeconds">Durée de validité de l'access token.</param>
public readonly record struct TokenPair(string AccessToken, string RefreshToken, int ExpiresInSeconds);

/// <summary>Génération des jetons d'authentification.</summary>
public interface ITokenService
{
    /// <summary>Crée un access token JWT pour l'utilisateur donné.</summary>
    string CreateAccessToken(Guid userId, string username, UserRole role);

    /// <summary>Génère un refresh token opaque et son hash de stockage.</summary>
    (string Token, string TokenHash) CreateRefreshToken();

    /// <summary>Recalcule le hash d'un refresh token reçu du client.</summary>
    string HashRefreshToken(string token);

    int AccessTokenLifetimeSeconds { get; }
    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>
/// Cache distribué optionnel. Toutes les méthodes doivent dégrader proprement lorsque
/// le backend est indisponible : le cache n'est jamais la source de vérité.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pose une marque si elle n'existe pas déjà. Retourne <c>true</c> si la marque a été posée,
    /// c'est-à-dire si l'appelant est le premier sur cette clé pendant la durée <paramref name="ttl"/>.
    /// Retourne <c>true</c> si le cache est indisponible, afin de ne jamais bloquer le métier.
    /// </summary>
    Task<bool> TryMarkAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
}
