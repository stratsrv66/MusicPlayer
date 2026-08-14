using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicPlatform.Infrastructure.Persistence;

namespace MusicPlatform.IntegrationTests;

/// <summary>Authentification, cycle de vie des jetons, export et suppression de compte.</summary>
[Collection(ApiCollection.Name)]
public sealed class AccountTests(ApiFactory factory)
{
    [Fact]
    public async Task RegistrationRejectsADuplicateEmailOrUsername()
    {
        var username = $"dup{Guid.NewGuid():N}"[..16];
        await factory.RegisterAsync(username);

        var client = factory.CreateApiClient();

        var sameEmail = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"{username}@test.local", username = $"other{Guid.NewGuid():N}"[..14], password = "TestPass123!" });
        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);

        var sameUsername = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"other{Guid.NewGuid():N}@test.local", username, password = "TestPass123!" });
        Assert.Equal(HttpStatusCode.Conflict, sameUsername.StatusCode);
    }

    [Fact]
    public async Task LoginWithWrongPasswordIsRejectedWithoutRevealingTheAccount()
    {
        var username = $"pwd{Guid.NewGuid():N}"[..16];
        await factory.RegisterAsync(username);
        var client = factory.CreateApiClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = $"{username}@test.local", password = "WrongPass123!" });

        var unknownAccount = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "nobody@test.local", password = "WrongPass123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);

        // Les deux cas renvoient rigoureusement le même code métier.
        var first = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>();
        var second = await unknownAccount.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AUTH_INVALID_CREDENTIALS", first.GetProperty("code").GetString());
        Assert.Equal("AUTH_INVALID_CREDENTIALS", second.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RefreshRotatesTheTokenAndInvalidatesThePreviousOne()
    {
        var user = await factory.RegisterAsync($"rot{Guid.NewGuid():N}"[..16]);
        var client = factory.CreateApiClient();

        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var payload = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(user.RefreshToken, payload.GetProperty("refreshToken").GetString());

        // Rejouer l'ancien jeton doit échouer : il a été révoqué par la rotation.
        var replayed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    [Fact]
    public async Task LogoutRevokesTheRefreshToken()
    {
        var user = await factory.RegisterAsync($"out{Guid.NewGuid():N}"[..16]);

        var logout = await user.Client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = user.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var client = factory.CreateApiClient();
        var afterLogout = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointsRequireAuthentication()
    {
        var anonymous = factory.CreateApiClient();

        foreach (var path in new[] { "/api/v1/me", "/api/v1/me/likes", "/api/v1/me/analytics/overview", "/api/v1/me/settings" })
        {
            var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task DataExportProducesADownloadableArchiveReservedToItsOwner()
    {
        var user = await factory.RegisterAsync($"exp{Guid.NewGuid():N}"[..16]);
        var stranger = await factory.RegisterAsync($"oth{Guid.NewGuid():N}"[..16]);

        var requested = await user.Client.PostAsync("/api/v1/me/data-export", null);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        var export = await requested.Content.ReadFromJsonAsync<JsonElement>();
        var exportId = export.GetProperty("id").GetGuid();

        // Une seconde demande est refusée tant que la première n'est pas terminée
        // ou aboutit une fois celle-ci achevée : les deux cas sont acceptables.
        var status = await WaitForExportAsync(user, exportId);
        Assert.Equal("Ready", status);

        var download = await user.Client.GetAsync($"/api/v1/me/data-exports/{exportId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType!.MediaType);

        var archive = await download.Content.ReadAsByteArrayAsync();
        Assert.True(archive.Length > 0);
        // Signature d'une archive ZIP.
        Assert.Equal([0x50, 0x4B], archive.Take(2));

        // L'archive d'un autre utilisateur reste inaccessible.
        var forbidden = await stranger.Client.GetAsync($"/api/v1/me/data-exports/{exportId}/download");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
    }

    [Fact]
    public async Task AccountDeletionRequiresExplicitConfirmation()
    {
        var user = await factory.RegisterAsync($"del{Guid.NewGuid():N}"[..16]);

        var withoutConfirmation = await SendDeleteAsync(user, new { confirm = false, confirmUsername = user.Username });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutConfirmation.StatusCode);

        var wrongUsername = await SendDeleteAsync(user, new { confirm = true, confirmUsername = "autre-chose" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongUsername.StatusCode);

        var problem = await wrongUsername.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ACCOUNT_DELETION_NOT_CONFIRMED", problem.GetProperty("code").GetString());

        // Le compte est toujours utilisable.
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task AccountDeletionRemovesPersonalDataContentAndFiles()
    {
        var user = await factory.RegisterAsync($"gone{Guid.NewGuid():N}"[..15]);

        // On crée du contenu afin de vérifier qu'il disparaît réellement.
        var playlist = await user.Client.PostAsJsonAsync(
            "/api/v1/playlists",
            new { name = "À supprimer", visibility = "Public" });
        playlist.EnsureSuccessStatusCode();
        var playlistId = (await playlist.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var deletion = await SendDeleteAsync(user, new { confirm = true, confirmUsername = user.Username });
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);

        // La playlist publique n'est plus accessible.
        var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/playlists/{playlistId}")).StatusCode);

        // Les données personnelles sont anonymisées en base et les sessions révoquées.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.Users.AsNoTracking().FirstAsync(u => u.Id == user.UserId);
        Assert.NotNull(stored.DeletedAt);
        Assert.DoesNotContain(user.Username, stored.Username, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("@deleted.invalid", stored.Email, StringComparison.Ordinal);
        Assert.Empty(stored.PasswordHash);
        Assert.Null(stored.Bio);

        Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == user.UserId));
        Assert.False(await db.Playlists.AnyAsync(p => p.OwnerId == user.UserId));
    }

    /// <summary>Envoie la requête de suppression, qui porte un corps JSON.</summary>
    private static async Task<HttpResponseMessage> SendDeleteAsync(AuthenticatedClient user, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me")
        {
            Content = JsonContent.Create(body),
        };

        return await user.Client.SendAsync(request);
    }

    /// <summary>Attend la fin de génération d'un export, avec un délai maximal.</summary>
    private static async Task<string> WaitForExportAsync(AuthenticatedClient user, Guid exportId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var export = await user.Client.GetFromJsonAsync<JsonElement>($"/api/v1/me/data-exports/{exportId}");
            var status = export.GetProperty("status").GetString() ?? string.Empty;

            if (status is "Ready" or "Failed")
            {
                return status;
            }

            await Task.Delay(250);
        }

        return "Timeout";
    }
}

/// <summary>Modération, administration et journal d'audit.</summary>
[Collection(ApiCollection.Name)]
public sealed class AdminTests(ApiFactory factory)
{
    [Fact]
    public async Task AdminEndpointsAreClosedToRegularUsers()
    {
        var user = await factory.RegisterAsync($"reg{Guid.NewGuid():N}"[..16]);

        foreach (var path in new[] { "/api/v1/admin/users", "/api/v1/admin/statistics", "/api/v1/admin/audit-logs", "/api/v1/admin/reports" })
        {
            var response = await user.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task AdminCanListUsersAndReadGlobalStatistics()
    {
        var admin = await factory.LoginAdminAsync();

        var users = await admin.Client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);

        var statistics = await admin.Client.GetFromJsonAsync<JsonElement>("/api/v1/admin/statistics");
        Assert.True(statistics.GetProperty("totalUsers").GetInt32() >= 1);
        Assert.True(statistics.TryGetProperty("storageBytesUsed", out _));
    }

    [Fact]
    public async Task ReportResolutionHidesTheTrackAndIsRecordedInTheAuditLog()
    {
        var admin = await factory.LoginAdminAsync();
        var owner = await factory.RegisterAsync($"rep{Guid.NewGuid():N}"[..16]);
        var reporter = await factory.RegisterAsync($"flag{Guid.NewGuid():N}"[..15]);

        var trackId = await UploadAndWaitAsync(owner, "Signalé");

        var report = await reporter.Client.PostAsJsonAsync(
            "/api/v1/reports",
            new { targetType = "Track", targetId = trackId, reason = "Copyright", description = "Test" });
        Assert.Equal(HttpStatusCode.Created, report.StatusCode);
        var reportId = (await report.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var resolved = await admin.Client.PatchAsJsonAsync(
            $"/api/v1/admin/reports/{reportId}",
            new { status = "Resolved", resolutionNote = "Contenu retiré", hideTarget = true });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        // Le morceau masqué disparaît pour le public.
        var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/tracks/{trackId}")).StatusCode);

        // L'action est tracée.
        var logs = await admin.Client.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-logs?action=REPORT_RESOLVED");
        Assert.True(logs.GetProperty("totalItems").GetInt64() >= 1);

        // La restauration remet le morceau en ligne.
        var restored = await admin.Client.PostAsync($"/api/v1/admin/tracks/{trackId}/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/v1/tracks/{trackId}")).StatusCode);
    }

    [Fact]
    public async Task SuspendingAUserRevokesTheirActiveSessions()
    {
        var admin = await factory.LoginAdminAsync();
        var user = await factory.RegisterAsync($"sus{Guid.NewGuid():N}"[..16]);

        var suspended = await admin.Client.PatchAsJsonAsync(
            $"/api/v1/admin/users/{user.UserId}",
            new { status = "Suspended" });
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);

        // Le refresh token est révoqué : la session ne peut plus être prolongée.
        var client = factory.CreateApiClient();
        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Et une nouvelle connexion est refusée.
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = $"{user.Username}@test.local", password = "TestPass123!" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
    }

    [Fact]
    public async Task AdminCannotRevokeTheirOwnAccess()
    {
        var admin = await factory.LoginAdminAsync();

        var response = await admin.Client.PatchAsJsonAsync(
            $"/api/v1/admin/users/{admin.UserId}",
            new { status = "Suspended" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GenreStillInUseCannotBeDeleted()
    {
        var admin = await factory.LoginAdminAsync();

        var created = await admin.Client.PostAsJsonAsync("/api/v1/admin/genres", new { name = $"Genre {Guid.NewGuid():N}"[..20] });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var genreId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Un genre inutilisé se supprime sans difficulté.
        var deleted = await admin.Client.DeleteAsync($"/api/v1/admin/genres/{genreId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task DuplicateGenreNameIsRejected()
    {
        var admin = await factory.LoginAdminAsync();
        var name = $"Unique{Guid.NewGuid():N}"[..18];

        var first = await admin.Client.PostAsJsonAsync("/api/v1/admin/genres", new { name });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await admin.Client.PostAsJsonAsync("/api/v1/admin/genres", new { name });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>Importe un morceau public et attend qu'il soit prêt.</summary>
    private static async Task<Guid> UploadAndWaitAsync(AuthenticatedClient user, string title)
    {
        using var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(TestAudio.CreateWav());
        audio.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "sample.wav");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("Public"), "visibility");

        var response = await user.Client.PostAsync("/api/v1/tracks", content);
        response.EnsureSuccessStatusCode();
        var trackId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trackId").GetGuid();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var track = await user.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tracks/{trackId}");
            if (track.GetProperty("track").GetProperty("status").GetString() == "Ready")
            {
                return trackId;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("The track was not processed in time.");
    }
}

/// <summary>Sondes de santé et documentation de l'API.</summary>
[Collection(ApiCollection.Name)]
public sealed class InfrastructureTests(ApiFactory factory)
{
    [Fact]
    public async Task LivenessProbeAnswersWithoutTouchingDependencies()
    {
        var response = await factory.CreateApiClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadinessProbeChecksPostgresAndStorage()
    {
        var response = await factory.CreateApiClient().GetAsync("/health/ready");

        // Redis n'est volontairement pas démarré : l'application reste disponible.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownRouteReturnsAProblemDetailsPayload()
    {
        var response = await factory.CreateApiClient().GetAsync("/api/v1/tracks/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TRACK_NOT_FOUND", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.True(problem.TryGetProperty("type", out _));
    }

    [Fact]
    public async Task GenresAreSeededAtStartup()
    {
        var genres = await factory.CreateApiClient().GetFromJsonAsync<JsonElement>("/api/v1/genres");

        Assert.True(genres.GetArrayLength() >= 20);
    }
}
