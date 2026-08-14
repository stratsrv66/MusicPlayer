using System.Security.Claims;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Api.Infrastructure;

/// <summary>
/// Expose l'identité de l'appelant à partir des claims du jeton d'accès.
/// Un jeton dont l'identifiant ou le rôle sont illisibles est traité comme anonyme.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public UserRole Role
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<UserRole>(value, ignoreCase: true, out var role) ? role : UserRole.User;
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => UserId is not null;

    /// <inheritdoc />
    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedException("Authentication is required to perform this action.");
}
