using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>users</c> : unicité de l'email et du pseudo normalisé.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.Username).HasMaxLength(32).IsRequired();
        builder.Property(u => u.UsernameNormalized).HasMaxLength(32).IsRequired();
        builder.Property(u => u.Bio).HasMaxLength(1000);
        builder.Property(u => u.SocialLinks).HasColumnType("jsonb");
        builder.Property(u => u.ProfileVisibility).HasMaxLength(16).IsRequired();
        builder.Property(u => u.Role).HasMaxLength(16).IsRequired();
        builder.Property(u => u.Status).HasMaxLength(16).IsRequired();

        // Verrou optimiste natif PostgreSQL : détecte les modifications concurrentes.
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.UsernameNormalized).IsUnique();
        builder.HasIndex(u => u.CreatedAt);

        builder.HasOne(u => u.AvatarFile)
            .WithMany()
            .HasForeignKey(u => u.AvatarFileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(u => u.Settings)
            .WithOne(s => s.User)
            .HasForeignKey<UserSettings>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>user_settings</c> : une ligne par utilisateur.</summary>
public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_settings");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.ShowLikeCount).IsRequired();
        builder.Property(s => s.ShowPlayCount).IsRequired();
    }
}

/// <summary>Table <c>refresh_tokens</c> : seul le hash du jeton est conservé.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>follows</c> : clé composite et interdiction de l'auto-abonnement.</summary>
public sealed class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("follows", table =>
            table.HasCheckConstraint("ck_follows_no_self_follow", "follower_id <> followed_id"));

        builder.HasKey(f => new { f.FollowerId, f.FollowedId });
        builder.HasIndex(f => f.FollowerId);
        builder.HasIndex(f => f.FollowedId);

        builder.HasOne(f => f.Follower)
            .WithMany(u => u.Following)
            .HasForeignKey(f => f.FollowerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Followed)
            .WithMany(u => u.Followers)
            .HasForeignKey(f => f.FollowedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Table <c>stored_files</c> : fichiers génériques (avatars, pochettes de playlist).</summary>
public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stored_files");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.StoragePath).HasMaxLength(512).IsRequired();
        builder.Property(f => f.MimeType).HasMaxLength(128).IsRequired();
        builder.HasIndex(f => f.StoragePath).IsUnique();
    }
}

/// <summary>Table <c>user_exports</c> : demandes d'export des données personnelles.</summary>
public sealed class UserExportConfiguration : IEntityTypeConfiguration<UserExport>
{
    public void Configure(EntityTypeBuilder<UserExport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_exports");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).HasMaxLength(16).IsRequired();
        builder.Property(e => e.StoragePath).HasMaxLength(512);
        builder.Property(e => e.FailureReason).HasMaxLength(512);

        builder.HasIndex(e => new { e.UserId, e.CreatedAt });

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
