using Microsoft.Extensions.DependencyInjection;
using MusicPlatform.Application.Features.Account;
using MusicPlatform.Application.Features.Admin;
using MusicPlatform.Application.Features.Analytics;
using MusicPlatform.Application.Features.Auth;
using MusicPlatform.Application.Features.Catalog;
using MusicPlatform.Application.Features.Comments;
using MusicPlatform.Application.Features.Discovery;
using MusicPlatform.Application.Features.Import;
using MusicPlatform.Application.Features.Moderation;
using MusicPlatform.Application.Features.Playback;
using MusicPlatform.Application.Features.Playlists;
using MusicPlatform.Application.Features.Search;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Application.Features.Users;

namespace MusicPlatform.Application;

/// <summary>Enregistrement des cas d'utilisation dans le conteneur d'injection de dépendances.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Enregistre tous les services applicatifs en portée « scoped » : ils partagent le
    /// <c>DbContext</c> de la requête courante, ce qui rend les transactions cohérentes.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<AccountService>();
        services.AddScoped<UserExportGenerator>();

        services.AddScoped<TagResolver>();
        services.AddScoped<TrackService>();
        services.AddScoped<TrackImportService>();
        services.AddScoped<TrackProcessingService>();

        services.AddScoped<TrackMatcher>();
        services.AddScoped<PlaylistImportService>();
        services.AddScoped<PlaylistImportRunner>();
        services.AddScoped<TrackStreamService>();
        services.AddScoped<TrackCoverService>();
        services.AddScoped<LikeService>();
        services.AddScoped<PlaybackService>();
        services.AddScoped<CommentService>();
        services.AddScoped<PlaylistService>();

        services.AddScoped<CatalogService>();
        services.AddScoped<ISearchService, PostgresSearchService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<HomeService>();
        services.AddScoped<AnalyticsService>();

        services.AddScoped<AuditLogger>();
        services.AddScoped<ReportService>();
        services.AddScoped<AdminService>();

        return services;
    }
}
