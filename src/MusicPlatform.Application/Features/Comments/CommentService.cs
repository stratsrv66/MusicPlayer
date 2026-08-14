using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Comments;

/// <summary>Cas d'utilisation CommentTrack : création, listage, modification et suppression.</summary>
public sealed class CommentService(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    /// <summary>Liste paginée des commentaires d'un morceau, du plus récent au plus ancien.</summary>
    public async Task<PagedResult<CommentDto>> ListAsync(Guid trackId, PageRequest page, CancellationToken cancellationToken)
    {
        var track = await LoadAccessibleTrackAsync(trackId, cancellationToken);

        var query = db.Comments
            .AsNoTracking()
            .Where(c => c.TrackId == trackId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new CommentRow(
                c.Id,
                c.TrackId,
                c.AuthorId,
                c.Author.Username,
                c.Author.AvatarFileId,
                c.Content,
                c.TimestampSeconds,
                c.CreatedAt,
                c.UpdatedAt));

        var result = await query.ToPagedResultAsync(page, cancellationToken);
        return result.Map(row => ToDto(row, track.OwnerId));
    }

    /// <summary>Poste un commentaire, éventuellement positionné dans le morceau.</summary>
    public async Task<CommentDto> CreateAsync(Guid trackId, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var track = await LoadAccessibleTrackAsync(trackId, cancellationToken);

        var content = NormalizeContent(request.Content);
        var timestamp = NormalizeTimestamp(request.TimestampSeconds, track.DurationSeconds);
        var now = clock.UtcNow;

        var comment = new Comment
        {
            TrackId = trackId,
            AuthorId = userId,
            Content = content,
            TimestampSeconds = timestamp,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        var author = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Username, u.AvatarFileId })
            .FirstAsync(cancellationToken);

        var row = new CommentRow(comment.Id, trackId, userId, author.Username, author.AvatarFileId,
            content, timestamp, now, now);

        return ToDto(row, track.OwnerId);
    }

    /// <summary>Modifie le texte d'un commentaire. Seul l'auteur en a le droit.</summary>
    public async Task<CommentDto> UpdateAsync(Guid commentId, UpdateCommentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        var comment = await LoadAsync(commentId, cancellationToken);

        if (!comment.IsEditableBy(userId))
        {
            throw new ForbiddenException("You can only edit your own comments.");
        }

        comment.Content = NormalizeContent(request.Content);
        comment.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var author = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == comment.AuthorId)
            .Select(u => new { u.Username, u.AvatarFileId })
            .FirstAsync(cancellationToken);

        var trackOwnerId = await db.Tracks
            .Where(t => t.Id == comment.TrackId)
            .Select(t => t.OwnerId)
            .FirstAsync(cancellationToken);

        var row = new CommentRow(comment.Id, comment.TrackId, comment.AuthorId, author.Username, author.AvatarFileId,
            comment.Content, comment.TimestampSeconds, comment.CreatedAt, comment.UpdatedAt);

        return ToDto(row, trackOwnerId);
    }

    /// <summary>
    /// Supprime un commentaire. L'auteur, le propriétaire du morceau et la modération
    /// en ont le droit. La suppression est logique afin de préserver les fils de discussion.
    /// </summary>
    public async Task DeleteAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var comment = await LoadAsync(commentId, cancellationToken);

        var trackOwnerId = await db.Tracks
            .Where(t => t.Id == comment.TrackId)
            .Select(t => t.OwnerId)
            .FirstAsync(cancellationToken);

        if (!comment.IsDeletableBy(userId, currentUser.Role, trackOwnerId))
        {
            throw new ForbiddenException("You are not allowed to delete this comment.");
        }

        comment.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Charge un commentaire non supprimé.</summary>
    private async Task<Comment> LoadAsync(Guid commentId, CancellationToken cancellationToken) =>
        await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && c.DeletedAt == null, cancellationToken)
        ?? throw new NotFoundException(ErrorCodes.CommentNotFound, "The requested comment does not exist.");

    /// <summary>Charge un morceau lisible par l'appelant.</summary>
    private async Task<Track> LoadAccessibleTrackAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == trackId && t.DeletedAt == null, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");

        if (!track.IsAccessibleBy(currentUser.UserId, currentUser.Role))
        {
            throw new NotFoundException(ErrorCodes.TrackNotFound, "The requested track does not exist.");
        }

        return track;
    }

    /// <summary>Valide et normalise le texte d'un commentaire.</summary>
    private static string NormalizeContent(string content)
    {
        var trimmed = content?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new InputValidationException("content", "A comment cannot be empty.");
        }

        if (trimmed.Length > Comment.MaxContentLength)
        {
            throw new InputValidationException("content", $"A comment cannot exceed {Comment.MaxContentLength} characters.");
        }

        return trimmed;
    }

    /// <summary>Refuse un timestamp négatif ou situé au-delà de la fin du morceau.</summary>
    private static int? NormalizeTimestamp(int? timestampSeconds, int trackDuration)
    {
        if (timestampSeconds is not { } timestamp)
        {
            return null;
        }

        if (timestamp < 0)
        {
            throw new InputValidationException("timestampSeconds", "The timestamp cannot be negative.");
        }

        if (trackDuration > 0 && timestamp > trackDuration)
        {
            throw new InputValidationException("timestampSeconds", "The timestamp is beyond the end of the track.");
        }

        return timestamp;
    }

    /// <summary>Convertit une ligne projetée en DTO avec les droits de l'appelant.</summary>
    private CommentDto ToDto(CommentRow row, Guid trackOwnerId)
    {
        var viewerId = currentUser.UserId;
        var canEdit = viewerId is not null && viewerId == row.AuthorId;
        var canDelete = viewerId is not null
                        && (canEdit || viewerId == trackOwnerId || currentUser.Role is UserRole.Moderator or UserRole.Admin);

        return new CommentDto
        {
            Id = row.Id,
            TrackId = row.TrackId,
            Author = new UserRefDto(row.AuthorId, row.AuthorUsername, MediaUrls.Avatar(row.AuthorAvatarFileId)),
            Content = row.Content,
            TimestampSeconds = row.TimestampSeconds,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            CanEdit = canEdit,
            CanDelete = canDelete,
        };
    }

    /// <summary>Ligne projetée depuis la base, avant application des droits.</summary>
    private sealed record CommentRow(
        Guid Id,
        Guid TrackId,
        Guid AuthorId,
        string AuthorUsername,
        Guid? AuthorAvatarFileId,
        string Content,
        int? TimestampSeconds,
        DateTime CreatedAt,
        DateTime UpdatedAt);
}
