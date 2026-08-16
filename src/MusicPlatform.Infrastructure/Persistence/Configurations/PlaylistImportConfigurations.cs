using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>playlist_imports</c> : suivi d'un import de playlist externe.</summary>
public sealed class PlaylistImportConfiguration : IEntityTypeConfiguration<PlaylistImport>
{
    public void Configure(EntityTypeBuilder<PlaylistImport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlist_imports");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Platform).HasMaxLength(16).IsRequired();
        builder.Property(i => i.SourcePlaylistId).HasMaxLength(200).IsRequired();
        builder.Property(i => i.SourceUrl).HasMaxLength(512);
        builder.Property(i => i.Name).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Status).HasMaxLength(16).IsRequired();
        builder.Property(i => i.Visibility).HasMaxLength(16).IsRequired();
        builder.Property(i => i.FailureReason).HasMaxLength(512);

        builder.HasIndex(i => new { i.UserId, i.CreatedAt });

        // Le service de reprise interroge les imports restés en cours après un arrêt.
        builder.HasIndex(i => i.Status);

        builder.HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // La suppression de la playlist miroir ne doit pas effacer l'historique de l'import.
        builder.HasOne(i => i.Playlist)
            .WithMany()
            .HasForeignKey(i => i.PlaylistId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Table <c>playlist_import_items</c> : un morceau inventorié par import.</summary>
public sealed class PlaylistImportItemConfiguration : IEntityTypeConfiguration<PlaylistImportItem>
{
    public void Configure(EntityTypeBuilder<PlaylistImportItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlist_import_items", table =>
            table.HasCheckConstraint("ck_playlist_import_items_duration_non_negative", "duration_seconds >= 0"));

        builder.HasKey(i => i.Id);

        builder.Property(i => i.SourceTrackId).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Title).HasMaxLength(300).IsRequired();
        builder.Property(i => i.ArtistName).HasMaxLength(300).IsRequired();
        builder.Property(i => i.SourceUrl).HasMaxLength(512);
        builder.Property(i => i.Status).HasMaxLength(16).IsRequired();
        builder.Property(i => i.FailureReason).HasMaxLength(512);

        // L'affichage liste les morceaux dans l'ordre de la playlist d'origine.
        builder.HasIndex(i => new { i.ImportId, i.Position });

        // Le runner ne reprend que les entrées non terminales d'un import.
        builder.HasIndex(i => new { i.ImportId, i.Status });

        builder.HasOne(i => i.Import)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.ImportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Un morceau supprimé de la bibliothèque laisse l'entrée d'import sans rattachement.
        builder.HasOne(i => i.Track)
            .WithMany()
            .HasForeignKey(i => i.TrackId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
