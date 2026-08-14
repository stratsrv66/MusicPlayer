using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>playlists</c>.</summary>
public sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlists");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Visibility).HasMaxLength(16).IsRequired();

        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(p => p.OwnerId);
        builder.HasIndex(p => new { p.Visibility, p.CreatedAt });

        builder.HasOne(p => p.Owner)
            .WithMany(u => u.Playlists)
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.CoverFile)
            .WithMany()
            .HasForeignKey(p => p.CoverFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Table <c>playlist_items</c> : clé composite (playlist, morceau).
/// Un morceau ne peut donc figurer qu'une seule fois dans une playlist, conformément
/// au modèle de données retenu.
/// </summary>
public sealed class PlaylistItemConfiguration : IEntityTypeConfiguration<PlaylistItem>
{
    public void Configure(EntityTypeBuilder<PlaylistItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlist_items", table =>
            table.HasCheckConstraint("ck_playlist_items_position_non_negative", "position >= 0"));

        builder.HasKey(i => new { i.PlaylistId, i.TrackId });
        builder.HasIndex(i => new { i.PlaylistId, i.Position });
        builder.HasIndex(i => i.TrackId);

        builder.HasOne(i => i.Playlist)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Track)
            .WithMany(t => t.PlaylistItems)
            .HasForeignKey(i => i.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>playlist_follows</c>.</summary>
public sealed class PlaylistFollowConfiguration : IEntityTypeConfiguration<PlaylistFollow>
{
    public void Configure(EntityTypeBuilder<PlaylistFollow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlist_follows");
        builder.HasKey(f => new { f.PlaylistId, f.UserId });
        builder.HasIndex(f => f.UserId);

        builder.HasOne(f => f.Playlist)
            .WithMany(p => p.Follows)
            .HasForeignKey(f => f.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>playlist_favorites</c>.</summary>
public sealed class PlaylistFavoriteConfiguration : IEntityTypeConfiguration<PlaylistFavorite>
{
    public void Configure(EntityTypeBuilder<PlaylistFavorite> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlist_favorites");
        builder.HasKey(f => new { f.PlaylistId, f.UserId });
        builder.HasIndex(f => f.UserId);

        builder.HasOne(f => f.Playlist)
            .WithMany(p => p.Favorites)
            .HasForeignKey(f => f.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>comments</c>.</summary>
public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("comments", table =>
            table.HasCheckConstraint("ck_comments_timestamp_non_negative", "timestamp_seconds IS NULL OR timestamp_seconds >= 0"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Content).HasMaxLength(2000).IsRequired();

        builder.HasIndex(c => new { c.TrackId, c.CreatedAt });
        builder.HasIndex(c => c.AuthorId);

        builder.HasOne(c => c.Track)
            .WithMany(t => t.Comments)
            .HasForeignKey(c => c.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Table <c>play_events</c> : source détaillée des statistiques.
/// L'auteur est effacé (SET NULL) lors d'une suppression de compte afin de conserver
/// les statistiques agrégées sans conserver de donnée personnelle.
/// </summary>
public sealed class PlayEventConfiguration : IEntityTypeConfiguration<PlayEvent>
{
    public void Configure(EntityTypeBuilder<PlayEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("play_events", table =>
            table.HasCheckConstraint("ck_play_events_duration_positive", "duration_seconds > 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Source).HasMaxLength(32);

        builder.HasIndex(p => new { p.TrackId, p.PlayedAt });
        builder.HasIndex(p => new { p.UserId, p.PlayedAt });
        builder.HasIndex(p => p.PlayedAt);

        builder.HasOne(p => p.Track)
            .WithMany()
            .HasForeignKey(p => p.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Table <c>listening_histories</c> : dernière position par couple utilisateur/morceau.</summary>
public sealed class ListeningHistoryConfiguration : IEntityTypeConfiguration<ListeningHistory>
{
    public void Configure(EntityTypeBuilder<ListeningHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("listening_histories", table =>
            table.HasCheckConstraint("ck_listening_histories_position_non_negative", "last_position_seconds >= 0"));

        builder.HasKey(h => h.Id);
        builder.HasIndex(h => new { h.UserId, h.TrackId }).IsUnique();
        builder.HasIndex(h => new { h.UserId, h.LastPlayedAt });

        builder.HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Track)
            .WithMany()
            .HasForeignKey(h => h.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>reports</c> : signalements de contenu.</summary>
public sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TargetType).HasMaxLength(16).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(16).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(16).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.Property(r => r.ResolutionNote).HasMaxLength(2000);

        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => new { r.TargetType, r.TargetId });
        builder.HasIndex(r => r.ReporterId);

        builder.HasOne(r => r.Reporter)
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Reviewer)
            .WithMany()
            .HasForeignKey(r => r.ReviewedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Table <c>audit_logs</c> : traçabilité des actions d'administration.</summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Action).HasMaxLength(64).IsRequired();
        builder.Property(l => l.TargetType).HasMaxLength(64);
        builder.Property(l => l.Metadata).HasColumnType("jsonb");

        builder.HasIndex(l => l.CreatedAt);
        builder.HasIndex(l => l.Action);

        builder.HasOne(l => l.Actor)
            .WithMany()
            .HasForeignKey(l => l.ActorId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
