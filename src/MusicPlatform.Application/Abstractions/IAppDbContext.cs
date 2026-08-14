using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Application.Abstractions;

/// <summary>
/// Accès en lecture/écriture au modèle relationnel.
/// L'interface vit dans Application afin que les cas d'utilisation restent testables sans
/// dépendre du <c>DbContext</c> concret défini dans Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Follow> Follows { get; }
    DbSet<StoredFile> StoredFiles { get; }

    DbSet<Track> Tracks { get; }
    DbSet<TrackFile> TrackFiles { get; }
    DbSet<TrackMetadata> TrackMetadata { get; }
    DbSet<TrackCover> TrackCovers { get; }
    DbSet<TrackTag> TrackTags { get; }
    DbSet<TrackLike> TrackLikes { get; }
    DbSet<UploadOperation> UploadOperations { get; }

    DbSet<Album> Albums { get; }
    DbSet<Genre> Genres { get; }
    DbSet<Tag> Tags { get; }

    DbSet<Playlist> Playlists { get; }
    DbSet<PlaylistItem> PlaylistItems { get; }
    DbSet<PlaylistFollow> PlaylistFollows { get; }
    DbSet<PlaylistFavorite> PlaylistFavorites { get; }

    DbSet<Comment> Comments { get; }
    DbSet<PlayEvent> PlayEvents { get; }
    DbSet<ListeningHistory> ListeningHistories { get; }

    DbSet<Report> Reports { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<UserExport> UserExports { get; }

    /// <summary>Donne accès aux transactions et aux commandes brutes.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Persiste les modifications en attente.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
