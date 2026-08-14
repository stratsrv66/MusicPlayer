using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MusicPlatform.Application.Abstractions;
using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Infrastructure.Persistence;

/// <summary>
/// Insère les données de référence indispensables au fonctionnement : la liste des genres
/// et, si la configuration le demande, un compte administrateur initial.
///
/// L'opération est idempotente : elle peut être rejouée à chaque démarrage sans effet de bord.
/// </summary>
public sealed class DatabaseSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IClock clock,
    IConfiguration configuration,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>Section de configuration décrivant le compte administrateur initial.</summary>
    public const string AdminSectionName = "Seed:Admin";

    /// <summary>Genres de référence proposés par la plateforme.</summary>
    private static readonly string[] DefaultGenres =
    [
        "Rock", "Pop", "Hip-Hop", "Electronic", "House", "Techno", "Jazz", "Blues",
        "Classical", "Metal", "Punk", "Reggae", "Soul", "Funk", "R&B", "Country",
        "Folk", "Ambient", "Drum & Bass", "Indie", "Latin", "World", "Soundtrack", "Experimental",
    ];

    /// <summary>Applique le peuplement initial.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedGenresAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
    }

    /// <summary>Ajoute les genres manquants sans toucher à ceux déjà présents.</summary>
    private async Task SeedGenresAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Genres.Select(g => g.Slug).ToListAsync(cancellationToken);
        var known = existing.ToHashSet(StringComparer.Ordinal);
        var added = 0;

        foreach (var name in DefaultGenres)
        {
            var slug = Tag.Normalize(name);
            if (!known.Add(slug))
            {
                continue;
            }

            db.Genres.Add(new Genre { Name = name, Slug = slug, CreatedAt = clock.UtcNow });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} genre(s).", added);
        }
    }

    /// <summary>
    /// Crée le compte administrateur défini par la configuration s'il n'existe pas déjà.
    /// Aucun identifiant n'est codé en dur : sans configuration, aucun compte n'est créé.
    /// </summary>
    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection(AdminSectionName);
        var email = section["Email"]?.Trim().ToLowerInvariant();
        var username = section["Username"]?.Trim();
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow;
        var admin = new User
        {
            Email = email,
            Username = username,
            UsernameNormalized = username.ToLowerInvariant(),
            PasswordHash = passwordHasher.Hash(password),
            Role = UserRole.Admin,
            Status = UserStatus.Active,
            ProfileVisibility = ProfileVisibility.Public,
            CreatedAt = now,
            UpdatedAt = now,
        };
        admin.Settings = new UserSettings { UserId = admin.Id, ShowLikeCount = true, ShowPlayCount = true };

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded administrator account {Username}.", username);
    }
}
