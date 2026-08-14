using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicPlatform.Api.Infrastructure;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Auth;

namespace MusicPlatform.Api.Controllers;

/// <summary>Inscription, connexion, renouvellement et révocation des jetons.</summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Authentication)]
public sealed class AuthController(AuthService authService) : ApiControllerBase
{
    /// <summary>Crée un compte et ouvre une session.</summary>
    /// <param name="request">Email, pseudo et mot de passe.</param>
    /// <response code="201">Compte créé, jetons émis.</response>
    /// <response code="400">Données invalides.</response>
    /// <response code="409">Email ou pseudo déjà utilisés.</response>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return Created($"/api/v1/users/{response.User.Username}", response);
    }

    /// <summary>Authentifie un utilisateur existant.</summary>
    /// <param name="request">Email et mot de passe.</param>
    /// <response code="200">Jetons émis.</response>
    /// <response code="401">Identifiants invalides.</response>
    /// <response code="403">Compte suspendu.</response>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.LoginAsync(request, cancellationToken));

    /// <summary>Échange un refresh token contre un nouveau couple de jetons.</summary>
    /// <param name="request">Refresh token en cours de validité.</param>
    /// <response code="200">Nouveaux jetons émis, l'ancien refresh token est révoqué.</response>
    /// <response code="401">Refresh token inconnu, expiré ou déjà utilisé.</response>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.RefreshAsync(request, cancellationToken));

    /// <summary>Révoque un refresh token. L'opération est idempotente.</summary>
    /// <param name="request">Refresh token à invalider.</param>
    /// <response code="204">Session close.</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;
        await authService.LogoutAsync(request, userId, cancellationToken);
        return NoContent();
    }
}
