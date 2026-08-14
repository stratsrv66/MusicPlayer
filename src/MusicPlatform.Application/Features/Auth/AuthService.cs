using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Users;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Auth;

/// <summary>
/// Cas d'utilisation d'authentification : RegisterUser, LoginUser, RefreshToken, LogoutUser.
/// </summary>
public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock,
    ILogger<AuthService> logger)
{
    /// <summary>Nombre maximal de refresh tokens actifs conservés par utilisateur.</summary>
    private const int MaxActiveRefreshTokensPerUser = 10;

    /// <summary>
    /// Crée un compte et ouvre immédiatement une session.
    /// Refuse un email ou un pseudo déjà utilisés.
    /// </summary>
    public async Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.Trim();
        var usernameNormalized = username.ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.AuthEmailTaken, "This email address is already registered.");
        }

        if (await db.Users.AnyAsync(u => u.UsernameNormalized == usernameNormalized, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.AuthUsernameTaken, "This username is already taken.");
        }

        var now = clock.UtcNow;
        var user = new User
        {
            Email = email,
            Username = username,
            UsernameNormalized = usernameNormalized,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = UserRole.User,
            Status = UserStatus.Active,
            ProfileVisibility = ProfileVisibility.Public,
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.Settings = new UserSettings { UserId = user.Id, ShowLikeCount = true, ShowPlayCount = true };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} registered.", user.Id);
        return await IssueTokensAsync(user, cancellationToken);
    }

    /// <summary>
    /// Authentifie un utilisateur. Le même message d'erreur est renvoyé pour un email inconnu
    /// et pour un mot de passe invalide, afin de ne pas révéler l'existence d'un compte.
    /// </summary>
    public async Task<AuthResponseDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null || user.DeletedAt is not null)
        {
            throw new UnauthorizedException("Invalid email or password.", ErrorCodes.AuthInvalidCredentials);
        }

        var (succeeded, needsRehash) = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (!succeeded)
        {
            logger.LogWarning("Failed login attempt for user {UserId}.", user.Id);
            throw new UnauthorizedException("Invalid email or password.", ErrorCodes.AuthInvalidCredentials);
        }

        if (user.Status == UserStatus.Suspended)
        {
            throw new ForbiddenException("This account has been suspended.", ErrorCodes.AuthAccountSuspended);
        }

        if (needsRehash)
        {
            // Le facteur de coût du hachage a évolué : on met le hash à niveau silencieusement.
            user.PasswordHash = passwordHasher.Hash(request.Password);
            user.UpdatedAt = clock.UtcNow;
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    /// <summary>
    /// Échange un refresh token contre un nouveau couple de jetons.
    /// L'ancien token est révoqué : un token ne peut donc servir qu'une fois.
    /// </summary>
    public async Task<AuthResponseDto> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var now = clock.UtcNow;

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || !stored.IsUsable(now))
        {
            throw new UnauthorizedException("The refresh token is invalid or expired.", ErrorCodes.AuthInvalidRefreshToken);
        }

        if (!stored.User.IsActive)
        {
            throw new UnauthorizedException("This account is no longer active.", ErrorCodes.AuthUnauthorized);
        }

        stored.RevokedAt = now;
        var response = await IssueTokensAsync(stored.User, cancellationToken, replacing: stored);
        return response;
    }

    /// <summary>Révoque un refresh token. L'opération est idempotente.</summary>
    public async Task LogoutAsync(RefreshTokenRequest request, Guid? userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Un token inconnu ou appartenant à un autre utilisateur est ignoré silencieusement :
        // le logout ne doit jamais révéler d'information ni échouer côté client.
        if (stored is null || (userId is not null && stored.UserId != userId))
        {
            return;
        }

        if (stored.RevokedAt is null)
        {
            stored.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Émet un access token et un refresh token, puis purge les jetons excédentaires.</summary>
    private async Task<AuthResponseDto> IssueTokensAsync(User user, CancellationToken cancellationToken, RefreshToken? replacing = null)
    {
        var now = clock.UtcNow;
        var (token, tokenHash) = tokenService.CreateRefreshToken();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(tokenService.RefreshTokenLifetime),
        };
        db.RefreshTokens.Add(refreshToken);

        if (replacing is not null)
        {
            replacing.ReplacedByTokenId = refreshToken.Id;
        }

        await PruneRefreshTokensAsync(user.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var profile = await db.Users
            .Where(u => u.Id == user.Id)
            .Project(user.Id)
            .FirstAsync(cancellationToken);

        return new AuthResponseDto(
            tokenService.CreateAccessToken(user.Id, user.Username, user.Role),
            token,
            tokenService.AccessTokenLifetimeSeconds,
            profile.ToProfileDto(user.Id, user.Role));
    }

    /// <summary>
    /// Supprime les tokens expirés ou révoqués et borne le nombre de sessions actives,
    /// afin que la table ne croisse pas indéfiniment.
    /// </summary>
    private async Task PruneRefreshTokensAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var obsolete = await db.RefreshTokens
            .Where(t => t.UserId == userId && (t.ExpiresAt <= now || t.RevokedAt != null))
            .ToListAsync(cancellationToken);

        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.ExpiresAt > now && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(MaxActiveRefreshTokensPerUser)
            .ToListAsync(cancellationToken);

        foreach (var token in obsolete)
        {
            db.RefreshTokens.Remove(token);
        }

        foreach (var token in active)
        {
            db.RefreshTokens.Remove(token);
        }
    }
}
