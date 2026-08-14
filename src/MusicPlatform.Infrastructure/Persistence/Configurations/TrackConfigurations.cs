using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>tracks</c> et ses index de listage.</summary>
public sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tracks", table =>
        {
            table.HasCheckConstraint("ck_tracks_duration_non_negative", "duration_seconds >= 0");
            table.HasCheckConstraint("ck_tracks_counters_non_negative", "like_count >= 0 AND play_count >= 0");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ArtistName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(5000);
        builder.Property(t => t.Visibility).HasMaxLength(16).IsRequired();
        builder.Property(t => t.Status).HasMaxLength(16).IsRequired();
        builder.Property(t => t.FailureReason).HasMaxLength(512);
        builder.Property(t => t.LikeCount).HasDefaultValue(0L);
        builder.Property(t => t.PlayCount).HasDefaultValue(0L);

        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(t => t.OwnerId);
        builder.HasIndex(t => new { t.Visibility, t.PublishedAt });
        builder.HasIndex(t => t.GenreId);
        builder.HasIndex(t => t.CreatedAt);
        builder.HasIndex(t => t.PlayCount);
        builder.HasIndex(t => t.AlbumId);

        builder.HasOne(t => t.Owner)
            .WithMany(u => u.Tracks)
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Album)
            .WithMany(a => a.Tracks)
            .HasForeignKey(t => t.AlbumId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Genre)
            .WithMany(g => g.Tracks)
            .HasForeignKey(t => t.GenreId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.File)
            .WithOne(f => f.Track)
            .HasForeignKey<TrackFile>(f => f.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Metadata)
            .WithOne(m => m.Track)
            .HasForeignKey<TrackMetadata>(m => m.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>track_files</c> : un fichier de diffusion par morceau.</summary>
public sealed class TrackFileConfiguration : IEntityTypeConfiguration<TrackFile>
{
    public void Configure(EntityTypeBuilder<TrackFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("track_files");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.StoragePath).HasMaxLength(512).IsRequired();
        builder.Property(f => f.MimeType).HasMaxLength(128).IsRequired();
        builder.Property(f => f.Checksum).HasMaxLength(64).IsRequired();
        builder.HasIndex(f => f.TrackId).IsUnique();
    }
}

/// <summary>Table <c>track_metadata</c> : métadonnées brutes du fichier d'origine.</summary>
public sealed class TrackMetadataConfiguration : IEntityTypeConfiguration<TrackMetadata>
{
    public void Configure(EntityTypeBuilder<TrackMetadata> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("track_metadata");
        builder.HasKey(m => m.TrackId);
        builder.Property(m => m.OriginalFilename).HasMaxLength(260);
        builder.Property(m => m.EmbeddedTitle).HasMaxLength(200);
        builder.Property(m => m.EmbeddedArtist).HasMaxLength(200);
        builder.Property(m => m.EmbeddedAlbum).HasMaxLength(200);
        builder.Property(m => m.EmbeddedGenre).HasMaxLength(100);
        builder.Property(m => m.MetadataJson).HasColumnType("jsonb");
    }
}

/// <summary>Table <c>track_covers</c> : une ligne par taille de pochette.</summary>
public sealed class TrackCoverConfiguration : IEntityTypeConfiguration<TrackCover>
{
    public void Configure(EntityTypeBuilder<TrackCover> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("track_covers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Size).HasMaxLength(16).IsRequired();
        builder.Property(c => c.StoragePath).HasMaxLength(512).IsRequired();
        builder.Property(c => c.MimeType).HasMaxLength(128).IsRequired();

        builder.HasIndex(c => new { c.TrackId, c.Size }).IsUnique();

        builder.HasOne(c => c.Track)
            .WithMany(t => t.Covers)
            .HasForeignKey(c => c.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>track_tags</c> : association many-to-many morceau/tag.</summary>
public sealed class TrackTagConfiguration : IEntityTypeConfiguration<TrackTag>
{
    public void Configure(EntityTypeBuilder<TrackTag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("track_tags");
        builder.HasKey(tt => new { tt.TrackId, tt.TagId });
        builder.HasIndex(tt => tt.TagId);

        builder.HasOne(tt => tt.Track)
            .WithMany(t => t.TrackTags)
            .HasForeignKey(tt => tt.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(tt => tt.Tag)
            .WithMany(t => t.TrackTags)
            .HasForeignKey(tt => tt.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>track_likes</c> : la clé composite empêche tout double like.</summary>
public sealed class TrackLikeConfiguration : IEntityTypeConfiguration<TrackLike>
{
    public void Configure(EntityTypeBuilder<TrackLike> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("track_likes");
        builder.HasKey(l => new { l.TrackId, l.UserId });
        builder.HasIndex(l => l.TrackId);
        builder.HasIndex(l => new { l.UserId, l.CreatedAt });

        builder.HasOne(l => l.Track)
            .WithMany(t => t.Likes)
            .HasForeignKey(l => l.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>upload_operations</c> : suivi et nettoyage des uploads.</summary>
public sealed class UploadOperationConfiguration : IEntityTypeConfiguration<UploadOperation>
{
    public void Configure(EntityTypeBuilder<UploadOperation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("upload_operations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Status).HasMaxLength(16).IsRequired();
        builder.Property(o => o.OriginalFilename).HasMaxLength(260).IsRequired();
        builder.Property(o => o.MimeType).HasMaxLength(128).IsRequired();
        builder.Property(o => o.TemporaryPath).HasMaxLength(512);
        builder.Property(o => o.FailureReason).HasMaxLength(512);

        builder.HasIndex(o => new { o.UserId, o.CreatedAt });
        builder.HasIndex(o => o.Status);

        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Track)
            .WithMany()
            .HasForeignKey(o => o.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>albums</c>.</summary>
public sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("albums");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ArtistName).HasMaxLength(200).IsRequired();
        builder.HasIndex(a => a.Name);

        builder.HasOne(a => a.Cover)
            .WithMany()
            .HasForeignKey(a => a.CoverId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Table <c>genres</c> : nom et slug uniques.</summary>
public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("genres");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(60).IsRequired();
        builder.Property(g => g.Slug).HasMaxLength(60).IsRequired();
        builder.HasIndex(g => g.Name).IsUnique();
        builder.HasIndex(g => g.Slug).IsUnique();
    }
}

/// <summary>Table <c>tags</c> : nom et slug uniques.</summary>
public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tags");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(50).IsRequired();
        builder.HasIndex(t => t.Name).IsUnique();
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
