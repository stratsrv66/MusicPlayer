using Microsoft.EntityFrameworkCore;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.Infrastructure.Persistence;

/// <summary>Contexte EF Core de la plateforme, unique point d'accès à PostgreSQL.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackFile> TrackFiles => Set<TrackFile>();
    public DbSet<TrackMetadata> TrackMetadata => Set<TrackMetadata>();
    public DbSet<TrackCover> TrackCovers => Set<TrackCover>();
    public DbSet<TrackTag> TrackTags => Set<TrackTag>();
    public DbSet<TrackLike> TrackLikes => Set<TrackLike>();
    public DbSet<UploadOperation> UploadOperations => Set<UploadOperation>();

    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<PlaylistFollow> PlaylistFollows => Set<PlaylistFollow>();
    public DbSet<PlaylistFavorite> PlaylistFavorites => Set<PlaylistFavorite>();

    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<PlayEvent> PlayEvents => Set<PlayEvent>();
    public DbSet<ListeningHistory> ListeningHistories => Set<ListeningHistory>();

    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserExport> UserExports => Set<UserExport>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Les enums sont stockés sous forme de chaînes : le schéma reste lisible et
        // l'ajout d'une valeur ne décale pas la signification des valeurs existantes.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type.IsEnum)
                {
                    property.SetProviderClrType(typeof(string));
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Toutes les dates sont manipulées en UTC de bout en bout.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}
