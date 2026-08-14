using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Infrastructure.Security;

/// <summary>Options de génération et de validation des jetons.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Clé de signature HMAC. Doit être fournie par configuration, jamais codée en dur.</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "musicplatform";
    public string Audience { get; set; } = "musicplatform";

    /// <summary>Durée de vie de l'access token, en secondes.</summary>
    public int AccessTokenLifetimeSeconds { get; set; } = 900;

    /// <summary>Durée de vie du refresh token, en jours.</summary>
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    /// <summary>Longueur minimale exigée pour la clé de signature.</summary>
    public const int MinimumSecretLength = 32;
}

/// <summary>
/// Hachage des mots de passe délégué à l'implémentation d'ASP.NET Core Identity
/// (PBKDF2-HMAC-SHA512, 100 000 itérations, sel aléatoire), afin de ne pas réécrire
/// de primitive cryptographique.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object HashingContext = new();

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _hasher.HashPassword(HashingContext, password);
    }

    /// <inheritdoc />
    public (bool Succeeded, bool NeedsRehash) Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return (false, false);
        }

        var result = _hasher.VerifyHashedPassword(HashingContext, hash, password);
        return result switch
        {
            PasswordVerificationResult.Success => (true, false),
            PasswordVerificationResult.SuccessRehashNeeded => (true, true),
            _ => (false, false),
        };
    }
}

/// <summary>Émission des access tokens JWT et des refresh tokens opaques.</summary>
public sealed class JwtTokenService : ITokenService
{
    /// <summary>Nombre d'octets aléatoires composant un refresh token.</summary>
    private const int RefreshTokenBytes = 48;

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        if (_options.Secret.Length < JwtOptions.MinimumSecretLength)
        {
            throw new InvalidOperationException(
                $"Jwt:Secret must be configured with at least {JwtOptions.MinimumSecretLength} characters.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public int AccessTokenLifetimeSeconds => _options.AccessTokenLifetimeSeconds;

    /// <inheritdoc />
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenLifetimeDays);

    /// <inheritdoc />
    public string CreateAccessToken(Guid userId, string username, UserRole role)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(_options.AccessTokenLifetimeSeconds),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public (string Token, string TokenHash) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashRefreshToken(token));
    }

    /// <inheritdoc />
    public string HashRefreshToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>Horloge système, remplaçable dans les tests.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
