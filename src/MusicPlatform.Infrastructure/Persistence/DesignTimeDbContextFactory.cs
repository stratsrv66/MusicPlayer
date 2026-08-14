using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MusicPlatform.Infrastructure.Persistence;

/// <summary>
/// Construit un contexte pour les commandes <c>dotnet ef</c>.
///
/// Les migrations sont générées à partir du modèle seul : aucune connexion n'est établie.
/// La chaîne utilisée est donc un simple gabarit, surchargeable par la variable
/// d'environnement <c>ConnectionStrings__Postgres</c> lorsqu'un accès réel est nécessaire.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=musicplatform;Username=musicplatform;Password=musicplatform";

    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(string.IsNullOrWhiteSpace(connectionString) ? FallbackConnectionString : connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
