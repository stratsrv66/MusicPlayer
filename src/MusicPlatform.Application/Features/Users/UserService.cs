using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Users;

/// <summary>
/// Cas d'utilisation liés au profil : consultation, mise à jour, avatar, préférences,
/// FollowUser et listes d'abonnés.
/// </summary>
public sealed class UserService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    IClock clock)
{
    /// <summary>Taille en pixels des avatars générés.</summary>
    private const int AvatarEdgePixels = 256;

    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 32;
    private const int MaxBioLength = 1000;
    private const int MaxSocialLinks = 8;

    /// <summary>Profil complet de l'utilisateur connecté.</summary>
    public async Task<MeDto> GetMeAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var projection = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Project(userId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The current user no longer exists.");

        return projection.ToMeDto();
    }

    /// <summary>Profil public d'un utilisateur identifié par son pseudo.</summary>
    public async Task<UserProfileDto> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var projection = await db.Users
            .AsNoTracking()
            .Where(u => u.UsernameNormalized == normalized && u.DeletedAt == null)
            .Project(currentUser.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");

        if (projection.Status == UserStatus.Suspended && currentUser.Role is not (UserRole.Moderator or UserRole.Admin))
        {
            throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");
        }

        return projection.ToProfileDto(currentUser.UserId, currentUser.Role);
    }

    /// <summary>Met à jour le profil de l'utilisateur connecté.</summary>
    public async Task<MeDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The current user no longer exists.");

        if (request.Username is not null)
        {
            await ApplyUsernameAsync(user, request.Username, cancellationToken);
        }

        if (request.Bio is not null)
        {
            var bio = request.Bio.Trim();
            if (bio.Length > MaxBioLength)
            {
                throw new InputValidationException("bio", $"The bio cannot exceed {MaxBioLength} characters.");
            }

            user.Bio = bio.Length == 0 ? null : bio;
        }

        if (request.SocialLinks is not null)
        {
            user.SocialLinks = UserMapper.SerializeSocialLinks(ValidateSocialLinks(request.SocialLinks));
        }

        if (request.ProfileVisibility is { } visibility)
        {
            user.ProfileVisibility = visibility;
        }

        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await GetMeAsync(cancellationToken);
    }

    /// <summary>Remplace l'avatar de l'utilisateur connecté.</summary>
    public async Task<MeDto> SetAvatarAsync(UploadedImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstAsync(u => u.Id == userId, cancellationToken);
        var previousFileId = user.AvatarFileId;

        var resized = imageProcessor.CreateSquare(image.Bytes, AvatarEdgePixels);
        var file = new StoredFile { MimeType = "image/webp", Width = resized.Width, Height = resized.Height, CreatedAt = clock.UtcNow };
        file.StoragePath = StoragePaths.Avatar(file.Id);

        using (var buffer = new MemoryStream(resized.Bytes, writable: false))
        {
            var written = await storage.SaveAsync(file.StoragePath, buffer, cancellationToken);
            file.FileSize = written.SizeBytes;
        }

        db.StoredFiles.Add(file);
        user.AvatarFileId = file.Id;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (previousFileId is { } oldId)
        {
            await DeleteStoredFileAsync(oldId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetMeAsync(cancellationToken);
    }

    /// <summary>Supprime l'avatar de l'utilisateur connecté.</summary>
    public async Task<MeDto> RemoveAvatarAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstAsync(u => u.Id == userId, cancellationToken);

        if (user.AvatarFileId is { } fileId)
        {
            user.AvatarFileId = null;
            user.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await DeleteStoredFileAsync(fileId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetMeAsync(cancellationToken);
    }

    /// <summary>Ouvre le fichier d'un avatar pour le renvoyer au client.</summary>
    public async Task<MediaStream> OpenAvatarAsync(Guid fileId, CancellationToken cancellationToken) =>
        await OpenStoredFileAsync(fileId, cancellationToken);

    /// <summary>Ouvre le fichier de pochette d'une playlist.</summary>
    public async Task<MediaStream> OpenPlaylistCoverAsync(Guid fileId, CancellationToken cancellationToken) =>
        await OpenStoredFileAsync(fileId, cancellationToken);

    /// <summary>Préférences de l'utilisateur connecté.</summary>
    public async Task<UserSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var settings = await db.UserSettings
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new UserSettingsDto(s.ShowLikeCount, s.ShowPlayCount))
            .FirstOrDefaultAsync(cancellationToken);

        return settings ?? new UserSettingsDto(true, true);
    }

    /// <summary>Met à jour les préférences d'affichage des compteurs.</summary>
    public async Task<UserSettingsDto> UpdateSettingsAsync(UpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (settings is null)
        {
            settings = new UserSettings { UserId = userId, ShowLikeCount = true, ShowPlayCount = true };
            db.UserSettings.Add(settings);
        }

        if (request.ShowLikeCount is { } showLikes)
        {
            settings.ShowLikeCount = showLikes;
        }

        if (request.ShowPlayCount is { } showPlays)
        {
            settings.ShowPlayCount = showPlays;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new UserSettingsDto(settings.ShowLikeCount, settings.ShowPlayCount);
    }

    /// <summary>Suit un utilisateur. L'opération est idempotente et refuse l'auto-abonnement.</summary>
    public async Task FollowAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await EnsureFollowTargetAsync(targetUserId, cancellationToken);

        if (await db.Follows.AnyAsync(f => f.FollowerId == userId && f.FollowedId == targetUserId, cancellationToken))
        {
            return;
        }

        var follow = Follow.Create(userId, targetUserId);
        follow.CreatedAt = clock.UtcNow;
        db.Follows.Add(follow);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Deux requêtes concurrentes : l'abonnement existe déjà, le résultat est le même.
            db.Follows.Remove(follow);
        }
    }

    /// <summary>Cesse de suivre un utilisateur. L'opération est idempotente.</summary>
    public async Task UnfollowAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await db.Follows
            .Where(f => f.FollowerId == userId && f.FollowedId == targetUserId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Abonnés d'un utilisateur.</summary>
    public async Task<PagedResult<UserSummaryDto>> ListFollowersAsync(Guid userId, PageRequest page, CancellationToken cancellationToken)
    {
        await EnsureProfileReadableAsync(userId, cancellationToken);

        var query = db.Follows
            .AsNoTracking()
            .Where(f => f.FollowedId == userId && f.Follower.DeletedAt == null)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Follower)
            .ProjectSummary();

        return await query.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Utilisateurs suivis par un utilisateur.</summary>
    public async Task<PagedResult<UserSummaryDto>> ListFollowingAsync(Guid userId, PageRequest page, CancellationToken cancellationToken)
    {
        await EnsureProfileReadableAsync(userId, cancellationToken);

        var query = db.Follows
            .AsNoTracking()
            .Where(f => f.FollowerId == userId && f.Followed.DeletedAt == null)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Followed)
            .ProjectSummary();

        return await query.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Valide et applique un changement de pseudo.</summary>
    private async Task ApplyUsernameAsync(User user, string requested, CancellationToken cancellationToken)
    {
        var username = requested.Trim();
        if (username.Length is < MinUsernameLength or > MaxUsernameLength)
        {
            throw new InputValidationException("username", $"The username must be between {MinUsernameLength} and {MaxUsernameLength} characters.");
        }

        foreach (var character in username)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('_' or '-' or '.'))
            {
                throw new InputValidationException("username", "The username may only contain letters, digits, '_', '-' and '.'.");
            }
        }

        var normalized = username.ToLowerInvariant();
        if (normalized == user.UsernameNormalized)
        {
            user.Username = username;
            return;
        }

        if (await db.Users.AnyAsync(u => u.UsernameNormalized == normalized && u.Id != user.Id, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.AuthUsernameTaken, "This username is already taken.");
        }

        user.Username = username;
        user.UsernameNormalized = normalized;
    }

    /// <summary>Borne le nombre et la longueur des liens sociaux, et n'accepte que http(s).</summary>
    private static Dictionary<string, string> ValidateSocialLinks(IReadOnlyDictionary<string, string> links)
    {
        if (links.Count > MaxSocialLinks)
        {
            throw new InputValidationException("socialLinks", $"At most {MaxSocialLinks} social links are allowed.");
        }

        var validated = new Dictionary<string, string>(links.Count);
        foreach (var (label, url) in links)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > 32)
            {
                throw new InputValidationException("socialLinks", "Each social link label must be between 1 and 32 characters.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InputValidationException("socialLinks", $"The link for '{label}' must be a valid http(s) URL.");
            }

            validated[label.Trim()] = uri.ToString();
        }

        return validated;
    }

    /// <summary>Vérifie que la cible d'un abonnement existe et est active.</summary>
    private async Task EnsureFollowTargetAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var exists = await db.Users.AnyAsync(
            u => u.Id == targetUserId && u.DeletedAt == null && u.Status == UserStatus.Active,
            cancellationToken);

        if (!exists)
        {
            throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");
        }
    }

    /// <summary>Refuse la lecture des listes d'abonnements d'un profil privé.</summary>
    private async Task EnsureProfileReadableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var target = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new { u.Id, u.ProfileVisibility })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user does not exist.");

        var isPrivileged = currentUser.UserId == target.Id || currentUser.Role is UserRole.Moderator or UserRole.Admin;
        if (target.ProfileVisibility == ProfileVisibility.Private && !isPrivileged)
        {
            throw new ForbiddenException("This profile is private.");
        }
    }

    /// <summary>Ouvre un fichier stocké générique (avatar, pochette de playlist).</summary>
    private async Task<MediaStream> OpenStoredFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.CoverNotFound, "The requested image does not exist.");

        var stat = await storage.StatAsync(file.StoragePath, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.CoverNotFound, "The image is missing from storage.");

        var content = await storage.OpenReadAsync(file.StoragePath, cancellationToken);
        return new MediaStream(content, stat.SizeBytes, file.MimeType, $"\"{fileId:N}\"", stat.LastModifiedUtc);
    }

    /// <summary>Déréférence puis supprime un fichier stocké.</summary>
    private async Task DeleteStoredFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null)
        {
            return;
        }

        db.StoredFiles.Remove(file);
        await storage.DeleteAsync(file.StoragePath, cancellationToken);
    }
}
