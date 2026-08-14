using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicPlatform.IntegrationTests;

/// <summary>Playlists : ajout, réordonnancement, duplication et permissions.</summary>
[Collection(ApiCollection.Name)]
public sealed class PlaylistTests(ApiFactory factory)
{
    [Fact]
    public async Task ReorderPersistsPositionsAndRejectsAnIncompleteRequest()
    {
        var owner = await factory.RegisterAsync($"pl{Guid.NewGuid():N}"[..16]);
        var playlistId = await CreatePlaylistAsync(owner, "Ma sélection", "Public");

        var trackIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var trackId = await UploadAndWaitAsync(owner, $"Piste {i}");
            trackIds.Add(trackId);

            var added = await owner.Client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/tracks", new { trackId });
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }

        // Un doublon est refusé par la contrainte de clé composite.
        var duplicate = await owner.Client.PostAsJsonAsync(
            $"/api/v1/playlists/{playlistId}/tracks",
            new { trackId = trackIds[0] });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Inversion complète de l'ordre.
        var reorder = await owner.Client.PatchAsJsonAsync(
            $"/api/v1/playlists/{playlistId}/tracks/reorder",
            new
            {
                items = new[]
                {
                    new { trackId = trackIds[2], position = 0 },
                    new { trackId = trackIds[1], position = 1 },
                    new { trackId = trackIds[0], position = 2 },
                },
            });
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);

        var tracks = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/playlists/{playlistId}/tracks");
        var ordered = tracks.EnumerateArray()
            .Select(item => item.GetProperty("track").GetProperty("id").GetGuid())
            .ToList();
        Assert.Equal([trackIds[2], trackIds[1], trackIds[0]], ordered);

        // Une demande partielle est rejetée par la règle de domaine.
        var partial = await owner.Client.PatchAsJsonAsync(
            $"/api/v1/playlists/{playlistId}/tracks/reorder",
            new { items = new[] { new { trackId = trackIds[0], position = 0 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, partial.StatusCode);
    }

    [Fact]
    public async Task RemovingATrackCompactsTheRemainingPositions()
    {
        var owner = await factory.RegisterAsync($"cmp{Guid.NewGuid():N}"[..16]);
        var playlistId = await CreatePlaylistAsync(owner, "Compactage", "Private");

        var trackIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var trackId = await UploadAndWaitAsync(owner, $"Compact {i}");
            trackIds.Add(trackId);
            await owner.Client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/tracks", new { trackId });
        }

        var removed = await owner.Client.DeleteAsync($"/api/v1/playlists/{playlistId}/tracks/{trackIds[0]}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var tracks = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/playlists/{playlistId}/tracks");
        var positions = tracks.EnumerateArray().Select(item => item.GetProperty("position").GetInt32()).ToList();

        Assert.Equal([0, 1], positions);
    }

    [Fact]
    public async Task PrivatePlaylistIsInvisibleAndNotEditableByOthers()
    {
        var owner = await factory.RegisterAsync($"pown{Guid.NewGuid():N}"[..15]);
        var stranger = await factory.RegisterAsync($"pstr{Guid.NewGuid():N}"[..15]);
        var anonymous = factory.CreateApiClient();

        var playlistId = await CreatePlaylistAsync(owner, "Confidentielle", "Private");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/v1/playlists/{playlistId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/playlists/{playlistId}")).StatusCode);

        var edit = await stranger.Client.PatchAsJsonAsync($"/api/v1/playlists/{playlistId}", new { name = "Détournée" });
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
    }

    [Fact]
    public async Task PublicPlaylistCanBeDuplicatedIntoAnotherAccount()
    {
        var owner = await factory.RegisterAsync($"dupo{Guid.NewGuid():N}"[..15]);
        var copier = await factory.RegisterAsync($"dupc{Guid.NewGuid():N}"[..15]);

        var playlistId = await CreatePlaylistAsync(owner, "Partageable", "Public");
        var trackId = await UploadAndWaitAsync(owner, "Partagée");
        await owner.Client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/tracks", new { trackId });

        var duplicated = await copier.Client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/duplicate", new { });
        Assert.Equal(HttpStatusCode.Created, duplicated.StatusCode);

        var copy = await duplicated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(copier.Username, copy.GetProperty("owner").GetProperty("username").GetString());
        Assert.Equal(1, copy.GetProperty("trackCount").GetInt32());

        // La copie est privée par défaut : dupliquer ne republie rien involontairement.
        Assert.Equal("Private", copy.GetProperty("visibility").GetString());
    }

    [Fact]
    public async Task FollowAndFavoriteArePerUserAndIdempotent()
    {
        var owner = await factory.RegisterAsync($"fvo{Guid.NewGuid():N}"[..16]);
        var fan = await factory.RegisterAsync($"fvf{Guid.NewGuid():N}"[..16]);

        var playlistId = await CreatePlaylistAsync(owner, "Suivie", "Public");

        await fan.Client.PostAsync($"/api/v1/playlists/{playlistId}/follow", null);
        var second = await fan.Client.PostAsync($"/api/v1/playlists/{playlistId}/follow", null);
        var state = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, state.GetProperty("followerCount").GetInt32());
        Assert.True(state.GetProperty("isFollowedByCurrentUser").GetBoolean());

        var favorited = await fan.Client.PostAsync($"/api/v1/playlists/{playlistId}/favorite", null);
        var favoriteState = await favorited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(favoriteState.GetProperty("isFavoritedByCurrentUser").GetBoolean());

        var favorites = await fan.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/favorites");
        Assert.Equal(1, favorites.GetProperty("totalItems").GetInt64());
    }

    /// <summary>Crée une playlist et retourne son identifiant.</summary>
    private static async Task<Guid> CreatePlaylistAsync(AuthenticatedClient user, string name, string visibility)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Importe un morceau public et attend la fin de son traitement.</summary>
    internal static async Task<Guid> UploadAndWaitAsync(AuthenticatedClient user, string title, params string[] tags)
    {
        using var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(TestAudio.CreateWav());
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "sample.wav");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("Public"), "visibility");

        foreach (var tag in tags)
        {
            content.Add(new StringContent(tag), "tags");
        }

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

/// <summary>Recherche, tags, profils et abonnements entre utilisateurs.</summary>
[Collection(ApiCollection.Name)]
public sealed class SearchAndSocialTests(ApiFactory factory)
{
    [Fact]
    public async Task SearchFindsTracksByTitleAndByTag()
    {
        var owner = await factory.RegisterAsync($"sch{Guid.NewGuid():N}"[..16]);
        var marker = $"zx{Guid.NewGuid():N}"[..10];

        await PlaylistTests.UploadAndWaitAsync(owner, $"Chanson {marker}", $"#{marker}", "Indie Rock");

        var anonymous = factory.CreateApiClient();

        var byTitle = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/search?q={marker}&type=Track");
        Assert.Equal(1, byTitle.GetProperty("tracks").GetProperty("totalItems").GetInt64());

        var byTag = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/search?q=%23{marker}&type=Track");
        Assert.Equal(1, byTag.GetProperty("tracks").GetProperty("totalItems").GetInt64());

        // Le tag « Indie Rock » a bien été normalisé en slug.
        var tagged = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/tags/indie-rock/tracks");
        Assert.True(tagged.GetProperty("totalItems").GetInt64() >= 1);
    }

    [Fact]
    public async Task SearchWithWildcardCharactersDoesNotMatchEverything()
    {
        var owner = await factory.RegisterAsync($"wld{Guid.NewGuid():N}"[..16]);
        await PlaylistTests.UploadAndWaitAsync(owner, "Un titre ordinaire");

        var anonymous = factory.CreateApiClient();
        var response = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/search?q=%25&type=Track");

        // « % » est échappé : il ne doit pas se comporter comme un joker.
        Assert.Equal(0, response.GetProperty("tracks").GetProperty("totalItems").GetInt64());
    }

    [Fact]
    public async Task FollowingAUserIsIdempotentAndSelfFollowIsRejected()
    {
        var artist = await factory.RegisterAsync($"art{Guid.NewGuid():N}"[..16]);
        var fan = await factory.RegisterAsync($"fn{Guid.NewGuid():N}"[..16]);

        Assert.Equal(HttpStatusCode.NoContent, (await fan.Client.PostAsync($"/api/v1/users/{artist.UserId}/follow", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fan.Client.PostAsync($"/api/v1/users/{artist.UserId}/follow", null)).StatusCode);

        var profile = await fan.Client.GetFromJsonAsync<JsonElement>($"/api/v1/users/{artist.Username}");
        Assert.Equal(1, profile.GetProperty("followerCount").GetInt32());
        Assert.True(profile.GetProperty("isFollowedByCurrentUser").GetBoolean());

        var selfFollow = await fan.Client.PostAsync($"/api/v1/users/{fan.UserId}/follow", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, selfFollow.StatusCode);

        var unfollowed = await fan.Client.DeleteAsync($"/api/v1/users/{artist.UserId}/follow");
        Assert.Equal(HttpStatusCode.NoContent, unfollowed.StatusCode);
    }

    [Fact]
    public async Task PrivateProfileHidesItsContentFromVisitors()
    {
        var user = await factory.RegisterAsync($"hid{Guid.NewGuid():N}"[..16]);
        var visitor = await factory.RegisterAsync($"vis{Guid.NewGuid():N}"[..16]);

        await PlaylistTests.UploadAndWaitAsync(user, "Morceau du profil privé");

        var updated = await user.Client.PatchAsJsonAsync("/api/v1/me", new { profileVisibility = "Private" });
        updated.EnsureSuccessStatusCode();

        var profile = await visitor.Client.GetFromJsonAsync<JsonElement>($"/api/v1/users/{user.Username}");
        Assert.True(profile.GetProperty("isRestricted").GetBoolean());
        Assert.Equal(0, profile.GetProperty("trackCount").GetInt32());

        var tracks = await visitor.Client.GetFromJsonAsync<JsonElement>($"/api/v1/users/{user.Username}/tracks");
        Assert.Equal(0, tracks.GetProperty("totalItems").GetInt64());

        // Le propriétaire continue de voir ses propres contenus.
        var own = await user.Client.GetFromJsonAsync<JsonElement>($"/api/v1/users/{user.Username}/tracks");
        Assert.Equal(1, own.GetProperty("totalItems").GetInt64());
    }

    [Fact]
    public async Task UsernameChangeIsRejectedWhenAlreadyTaken()
    {
        var first = await factory.RegisterAsync($"one{Guid.NewGuid():N}"[..16]);
        var second = await factory.RegisterAsync($"two{Guid.NewGuid():N}"[..16]);

        var response = await second.Client.PatchAsJsonAsync("/api/v1/me", new { username = first.Username });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PlaybackProgressIsStoredAndReturnedForResume()
    {
        var owner = await factory.RegisterAsync($"prg{Guid.NewGuid():N}"[..16]);
        var trackId = await PlaylistTests.UploadAndWaitAsync(owner, "Reprise");

        var saved = await owner.Client.PutAsJsonAsync($"/api/v1/tracks/{trackId}/progress", new { positionSeconds = 7 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var progress = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tracks/{trackId}/progress");
        Assert.Equal(7, progress.GetProperty("positionSeconds").GetInt32());

        // La position est bornée par la durée réelle du morceau.
        await owner.Client.PutAsJsonAsync($"/api/v1/tracks/{trackId}/progress", new { positionSeconds = 99999 });
        var clamped = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tracks/{trackId}/progress");
        Assert.True(clamped.GetProperty("positionSeconds").GetInt32() <= 60);

        var history = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/history");
        Assert.Equal(1, history.GetProperty("totalItems").GetInt64());
    }

    [Fact]
    public async Task ArtistAnalyticsCoverOwnedTracksOnly()
    {
        var artist = await factory.RegisterAsync($"anl{Guid.NewGuid():N}"[..16]);
        var listener = await factory.RegisterAsync($"lis{Guid.NewGuid():N}"[..16]);

        var trackId = await PlaylistTests.UploadAndWaitAsync(artist, "Mesurée");

        await listener.Client.PostAsJsonAsync(
            $"/api/v1/tracks/{trackId}/plays",
            new { sessionId = Guid.NewGuid(), positionSeconds = 11, durationSeconds = 11, source = "PLAYER" });
        await listener.Client.PostAsync($"/api/v1/tracks/{trackId}/like", null);

        var overview = await artist.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/analytics/overview");
        Assert.Equal(1, overview.GetProperty("trackCount").GetInt32());
        Assert.Equal(1, overview.GetProperty("totalPlays").GetInt64());
        Assert.Equal(1, overview.GetProperty("totalLikes").GetInt64());

        // L'auditeur ne possède aucun morceau : ses statistiques sont vides.
        var otherOverview = await listener.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/analytics/overview");
        Assert.Equal(0, otherOverview.GetProperty("trackCount").GetInt32());
        Assert.Equal(0, otherOverview.GetProperty("totalPlays").GetInt64());

        var series = await artist.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/analytics/plays?groupBy=Day");
        Assert.True(series.GetProperty("points").GetArrayLength() >= 1);
    }
}
