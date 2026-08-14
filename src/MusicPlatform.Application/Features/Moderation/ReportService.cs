using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Features.Moderation;

/// <summary>
/// Cas d'utilisation de signalement et de modération : création par un utilisateur,
/// consultation et traitement (ProcessReport) par un modérateur ou un administrateur.
/// </summary>
public sealed class ReportService(IAppDbContext db, ICurrentUser currentUser, AuditLogger audit, IClock clock)
{
    /// <summary>Longueur maximale de la description d'un signalement.</summary>
    private const int MaxDescriptionLength = 2000;

    /// <summary>Crée un signalement après avoir vérifié que la cible existe réellement.</summary>
    public async Task<ReportDto> CreateAsync(CreateReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();
        await EnsureTargetExistsAsync(request.TargetType, request.TargetId, cancellationToken);

        var description = request.Description?.Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            throw new InputValidationException("description", $"The description cannot exceed {MaxDescriptionLength} characters.");
        }

        var existing = await db.Reports.FirstOrDefaultAsync(
            r => r.ReporterId == userId
                 && r.TargetType == request.TargetType
                 && r.TargetId == request.TargetId
                 && r.Status == ReportStatus.Pending,
            cancellationToken);

        if (existing is not null)
        {
            // Un utilisateur ne peut pas empiler les signalements identiques encore ouverts.
            return await ReadAsync(existing.Id, includeReporter: false, cancellationToken);
        }

        var report = new Report
        {
            ReporterId = userId,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            Reason = request.Reason,
            Description = string.IsNullOrEmpty(description) ? null : description,
            Status = ReportStatus.Pending,
            CreatedAt = clock.UtcNow,
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        return await ReadAsync(report.Id, includeReporter: false, cancellationToken);
    }

    /// <summary>Signalements créés par l'utilisateur connecté.</summary>
    public async Task<PagedResult<ReportDto>> ListMineAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var query = db.Reports
            .AsNoTracking()
            .Where(r => r.ReporterId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(Projection(false));

        return await query.ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Liste filtrée des signalements, réservée à la modération.</summary>
    public async Task<PagedResult<ReportDto>> ListForModerationAsync(ReportFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        EnsureCanModerate();

        var query = db.Reports.AsNoTracking().AsQueryable();

        if (filter.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        if (filter.Reason is { } reason)
        {
            query = query.Where(r => r.Reason == reason);
        }

        if (filter.TargetType is { } targetType)
        {
            query = query.Where(r => r.TargetType == targetType);
        }

        if (filter.From is { } from)
        {
            query = query.Where(r => r.CreatedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(r => r.CreatedAt <= to);
        }

        var projected = query
            .OrderBy(r => r.Status)
            .ThenByDescending(r => r.CreatedAt)
            .Select(Projection(true));

        var result = await projected.ToPagedResultAsync(page, cancellationToken);
        return await EnrichTargetLabelsAsync(result, cancellationToken);
    }

    /// <summary>Détail d'un signalement pour la modération.</summary>
    public async Task<ReportDto> GetForModerationAsync(Guid reportId, CancellationToken cancellationToken)
    {
        EnsureCanModerate();
        var report = await ReadAsync(reportId, includeReporter: true, cancellationToken);
        var enriched = await EnrichTargetLabelsAsync(
            new PagedResult<ReportDto> { Items = [report], Page = 1, PageSize = 1, TotalItems = 1 },
            cancellationToken);

        return enriched.Items[0];
    }

    /// <summary>
    /// Traite un signalement : change son statut, enregistre la justification et masque
    /// éventuellement le contenu visé. L'ensemble est transactionnel et tracé dans l'audit.
    /// </summary>
    public async Task<ReportDto> ResolveAsync(Guid reportId, ResolveReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanModerate();

        if (request.Status == ReportStatus.Pending)
        {
            throw new InputValidationException("status", "A report cannot be moved back to PENDING.");
        }

        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.ReportNotFound, "The requested report does not exist.");

        var moderatorId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        report.Status = request.Status;
        report.ResolutionNote = request.ResolutionNote?.Trim();
        report.ReviewedBy = moderatorId;
        report.ReviewedAt = now;

        if (request.HideTarget)
        {
            await HideTargetAsync(report, now, cancellationToken);
        }

        await audit.RecordAsync(
            "REPORT_RESOLVED",
            report.TargetType.ToString(),
            report.TargetId,
            new { reportId, status = request.Status.ToString(), hidden = request.HideTarget },
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetForModerationAsync(reportId, cancellationToken);
    }

    /// <summary>Masque le contenu visé par un signalement selon son type.</summary>
    private async Task HideTargetAsync(Report report, DateTime now, CancellationToken cancellationToken)
    {
        switch (report.TargetType)
        {
            case ReportTargetType.Track:
                await db.Tracks
                    .Where(t => t.Id == report.TargetId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.HiddenAt, now), cancellationToken);
                break;

            case ReportTargetType.Comment:
                await db.Comments
                    .Where(c => c.Id == report.TargetId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, now), cancellationToken);
                break;

            case ReportTargetType.User:
                await db.Users
                    .Where(u => u.Id == report.TargetId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended), cancellationToken);
                break;

            case ReportTargetType.Playlist:
                await db.Playlists
                    .Where(p => p.Id == report.TargetId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Visibility, ContentVisibility.Private), cancellationToken);
                break;

            default:
                throw new InputValidationException("targetType", "Unsupported report target type.");
        }
    }

    /// <summary>Vérifie que la cible du signalement existe avant de l'accepter.</summary>
    private async Task EnsureTargetExistsAsync(ReportTargetType targetType, Guid targetId, CancellationToken cancellationToken)
    {
        var exists = targetType switch
        {
            ReportTargetType.Track => await db.Tracks.AnyAsync(t => t.Id == targetId && t.DeletedAt == null, cancellationToken),
            ReportTargetType.Comment => await db.Comments.AnyAsync(c => c.Id == targetId && c.DeletedAt == null, cancellationToken),
            ReportTargetType.User => await db.Users.AnyAsync(u => u.Id == targetId && u.DeletedAt == null, cancellationToken),
            ReportTargetType.Playlist => await db.Playlists.AnyAsync(p => p.Id == targetId, cancellationToken),
            _ => false,
        };

        if (!exists)
        {
            throw new NotFoundException(ErrorCodes.ReportTargetNotFound, "The reported content does not exist.");
        }
    }

    /// <summary>Complète les signalements avec un libellé lisible de leur cible.</summary>
    private async Task<PagedResult<ReportDto>> EnrichTargetLabelsAsync(PagedResult<ReportDto> source, CancellationToken cancellationToken)
    {
        if (source.Items.Count == 0)
        {
            return source;
        }

        var labels = new Dictionary<Guid, string>();
        await AddLabelsAsync(labels, source, ReportTargetType.Track,
            ids => db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => new LabelRow(t.Id, t.Title)), cancellationToken);
        await AddLabelsAsync(labels, source, ReportTargetType.Comment,
            ids => db.Comments.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new LabelRow(c.Id, c.Content)), cancellationToken);
        await AddLabelsAsync(labels, source, ReportTargetType.User,
            ids => db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new LabelRow(u.Id, u.Username)), cancellationToken);
        await AddLabelsAsync(labels, source, ReportTargetType.Playlist,
            ids => db.Playlists.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new LabelRow(p.Id, p.Name)), cancellationToken);

        return source.Map(report => labels.TryGetValue(report.TargetId, out var label)
            ? report with { TargetLabel = Shorten(label) }
            : report);
    }

    /// <summary>Charge en une requête les libellés d'un type de cible donné.</summary>
    private static async Task AddLabelsAsync(
        Dictionary<Guid, string> labels,
        PagedResult<ReportDto> source,
        ReportTargetType targetType,
        Func<List<Guid>, IQueryable<LabelRow>> queryFactory,
        CancellationToken cancellationToken)
    {
        var ids = source.Items.Where(r => r.TargetType == targetType).Select(r => r.TargetId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var rows = await queryFactory(ids).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            labels[row.Id] = row.Label;
        }
    }

    /// <summary>Lit un signalement dont l'appelant est l'auteur ou qu'il peut modérer.</summary>
    private async Task<ReportDto> ReadAsync(Guid reportId, bool includeReporter, CancellationToken cancellationToken)
    {
        var reporterId = await db.Reports
            .AsNoTracking()
            .Where(r => r.Id == reportId)
            .Select(r => (Guid?)r.ReporterId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.ReportNotFound, "The requested report does not exist.");

        if (currentUser.UserId != reporterId && !currentUser.Role.CanModerate())
        {
            throw new ForbiddenException("You are not allowed to view this report.");
        }

        return await db.Reports
            .AsNoTracking()
            .Where(r => r.Id == reportId)
            .Select(Projection(includeReporter))
            .FirstAsync(cancellationToken);
    }

    /// <summary>Refuse l'accès aux fonctions de modération.</summary>
    private void EnsureCanModerate()
    {
        if (!currentUser.Role.CanModerate())
        {
            throw new ForbiddenException("Moderation privileges are required.");
        }
    }

    /// <summary>
    /// Projections SQL d'un signalement vers son DTO.
    ///
    /// Ce sont des arbres d'expression et non des méthodes : EF Core doit pouvoir les
    /// traduire en SQL, ce qu'un appel de méthode déréférençant une navigation empêcherait.
    /// </summary>
    private static readonly Expression<Func<Report, ReportDto>> ProjectWithReporter = r => new ReportDto
    {
        Id = r.Id,
        TargetType = r.TargetType,
        TargetId = r.TargetId,
        Reason = r.Reason,
        Description = r.Description,
        Status = r.Status,
        ResolutionNote = r.ResolutionNote,
        CreatedAt = r.CreatedAt,
        ReviewedAt = r.ReviewedAt,
        Reporter = new UserRefDto(
            r.ReporterId,
            r.Reporter.Username,
            r.Reporter.AvatarFileId == null ? null : MediaUrls.Base + "/media/avatars/" + r.Reporter.AvatarFileId),
    };

    /// <summary>Projection sans l'auteur, utilisée pour la vue de l'auteur lui-même.</summary>
    private static readonly Expression<Func<Report, ReportDto>> ProjectWithoutReporter = r => new ReportDto
    {
        Id = r.Id,
        TargetType = r.TargetType,
        TargetId = r.TargetId,
        Reason = r.Reason,
        Description = r.Description,
        Status = r.Status,
        ResolutionNote = r.ResolutionNote,
        CreatedAt = r.CreatedAt,
        ReviewedAt = r.ReviewedAt,
        Reporter = null,
    };

    /// <summary>Choisit la projection selon que l'auteur doit être exposé ou non.</summary>
    private static Expression<Func<Report, ReportDto>> Projection(bool includeReporter) =>
        includeReporter ? ProjectWithReporter : ProjectWithoutReporter;

    /// <summary>Tronque un libellé pour l'affichage dans les listes de modération.</summary>
    private static string Shorten(string label) => label.Length <= 80 ? label : label[..77] + "...";

    /// <summary>Ligne identifiant/libellé utilisée pour enrichir les signalements.</summary>
    private sealed record LabelRow(Guid Id, string Label);
}

/// <summary>Écrit les actions sensibles dans le journal d'audit.</summary>
public sealed class AuditLogger(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Enregistre une action d'administration. L'entrée n'est pas persistée immédiatement :
    /// elle participe à la transaction de l'appelant.
    /// </summary>
    public Task RecordAsync(string action, string? targetType, Guid? targetId, object? metadata, CancellationToken cancellationToken)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = currentUser.UserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata, MetadataOptions),
            CreatedAt = clock.UtcNow,
        });

        return Task.CompletedTask;
    }
}

/// <summary>Extensions de lisibilité sur les rôles.</summary>
public static class UserRoleExtensions
{
    /// <summary>Vrai si le rôle donne accès aux fonctions de modération.</summary>
    public static bool CanModerate(this UserRole role) => role is UserRole.Moderator or UserRole.Admin;

    /// <summary>Vrai si le rôle donne accès aux fonctions d'administration.</summary>
    public static bool IsAdmin(this UserRole role) => role == UserRole.Admin;
}
