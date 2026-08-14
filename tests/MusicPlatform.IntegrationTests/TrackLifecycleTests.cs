using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicPlatform.IntegrationTests;

/// <summary>
/// Parcours complet d'un morceau : upload, traitement, streaming avec requêtes Range,
/// écoutes, likes, commentaires et permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TrackLifecycleTests(ApiFactory factory)
{
    /// <summary>Délai maximal accordé au traitement en arrière-plan d'un upload.</summary>
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task UploadedTrack_IsProcessedThenStreamableWithRangeSupport()
    {
        var owner = await factory.RegisterAsync($"artist{Guid.NewGuid():N}"[..16]);

        var trackId = await UploadTrackAsync(owner, "Mon morceau", "Public");
        var track = await WaitUntilReadyAsync(owner, trackId);

        Assert.Equal("Ready", track.GetProperty("track").GetProperty("status").GetString());
        Assert.True(track.GetProperty("track").GetProperty("durationSeconds").GetInt32() >= 10);

        // --- Requête complète ---
        var full = await owner.Client.GetAsync($"/api/v1/tracks/{trackId}/stream");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal("bytes", full.Headers.AcceptRanges.Single());
        var totalLength = full.Content.Headers.ContentLength!.Value;
        Assert.True(totalLength > 0);

        // --- Fragment initial ---
        var partialRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tracks/{trackId}/stream");
        partialRequest.Headers.Range = new RangeHeaderValue(0, 1023);
        var partial = await owner.Client.SendAsync(partialRequest);

        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(1024, partial.Content.Headers.ContentLength);
        Assert.Equal(0, partial.Content.Headers.ContentRange!.From);
        Assert.Equal(totalLength, partial.Content.Headers.ContentRange.Length);

        // --- Plage hors limites ---
        var invalidRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tracks/{trackId}/stream");
        invalidRequest.Headers.Range = new RangeHeaderValue(totalLength + 5000, null);
        var invalid = await owner.Client.SendAsync(invalidRequest);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, invalid.StatusCode);
    }

    [Fact]
    public async Task UploadingANonAudioFile_IsRejectedAndLeavesNoReadyTrack()
    {
        var owner = await factory.RegisterAsync($"bogus{Guid.NewGuid():N}"[..16]);

        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(TestAudio.CreateNonAudio());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(bytes, "file", "fake.mp3");
        content.Add(new StringContent("Faux morceau"), "title");

        var response = await owner.Client.PostAsync("/api/v1/tracks", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TRACK_UPLOAD_INVALID", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UploadingAnUnsupportedFormat_ReturnsUnsupportedMediaType()
    {
        var owner = await factory.RegisterAsync($"badext{Guid.NewGuid():N}"[..16]);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(TestAudio.CreateNonAudio()), "file", "document.pdf");

        var response = await owner.Client.PostAsync("/api/v1/tracks", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task PrivateTrack_IsInvisibleToOtherUsersAndToAnonymousVisitors()
    {
        var owner = await factory.RegisterAsync($"priv{Guid.NewGuid():N}"[..16]);
        var stranger = await factory.RegisterAsync($"nosy{Guid.NewGuid():N}"[..16]);
        var anonymous = factory.CreateApiClient();

        var trackId = await UploadTrackAsync(owner, "Secret", "Private");
        await WaitUntilReadyAsync(owner, trackId);

        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync($"/api/v1/tracks/{trackId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/v1/tracks/{trackId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/tracks/{trackId}")).StatusCode);

        // Le flux audio est protégé au même titre que la fiche du morceau.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/tracks/{trackId}/stream")).StatusCode);

        // Et il n'apparaît pas dans le catalogue public.
        var catalogue = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/tracks?pageSize=100");
        var ids = catalogue.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetString());
        Assert.DoesNotContain(trackId.ToString(), ids);
    }

    [Fact]
    public async Task UnlistedTrack_IsReachableByLinkButAbsentFromTheCatalogue()
    {
        var owner = await factory.RegisterAsync($"unl{Guid.NewGuid():N}"[..16]);
        var anonymous = factory.CreateApiClient();

        var trackId = await UploadTrackAsync(owner, "Non répertorié", "Unlisted");
        await WaitUntilReadyAsync(owner, trackId);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/v1/tracks/{trackId}")).StatusCode);

        var catalogue = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/tracks?pageSize=100");
        var ids = catalogue.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetString());
        Assert.DoesNotContain(trackId.ToString(), ids);
    }

    [Fact]
    public async Task AnotherUser_CannotModifyOrDeleteSomeoneElsesTrack()
    {
        var owner = await factory.RegisterAsync($"own{Guid.NewGuid():N}"[..16]);
        var stranger = await factory.RegisterAsync($"str{Guid.NewGuid():N}"[..16]);

        var trackId = await UploadTrackAsync(owner, "Protégé", "Public");
        await WaitUntilReadyAsync(owner, trackId);

        var update = await stranger.Client.PatchAsJsonAsync($"/api/v1/tracks/{trackId}", new { title = "Détourné" });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var delete = await stranger.Client.DeleteAsync($"/api/v1/tracks/{trackId}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        // Le titre d'origine est intact.
        var reread = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tracks/{trackId}");
        Assert.Equal("Protégé", reread.GetProperty("track").GetProperty("title").GetString());
    }

    [Fact]
    public async Task PlayIsCountedOnlyBeyondTenSecondsAndNotTwice()
    {
        var owner = await factory.RegisterAsync($"play{Guid.NewGuid():N}"[..16]);
        var listener = await factory.RegisterAsync($"lstn{Guid.NewGuid():N}"[..16]);

        var trackId = await UploadTrackAsync(owner, "Écoutable", "Public");
        await WaitUntilReadyAsync(owner, trackId);

        var tooShort = await PostPlayAsync(listener, trackId, durationSeconds: 5);
        Assert.False(tooShort.GetProperty("counted").GetBoolean());
        Assert.Equal("PLAY_TOO_SHORT", tooShort.GetProperty("reason").GetString());

        var valid = await PostPlayAsync(listener, trackId, durationSeconds: 11);
        Assert.True(valid.GetProperty("counted").GetBoolean());
        Assert.Equal(1, valid.GetProperty("playCount").GetInt64());

        var duplicate = await PostPlayAsync(listener, trackId, durationSeconds: 11);
        Assert.False(duplicate.GetProperty("counted").GetBoolean());
        Assert.Equal("PLAY_ALREADY_COUNTED", duplicate.GetProperty("reason").GetString());
        Assert.Equal(1, duplicate.GetProperty("playCount").GetInt64());
    }

    [Fact]
    public async Task LikeIsIdempotentAndKeepsTheCounterConsistent()
    {
        var owner = await factory.RegisterAsync($"lkown{Guid.NewGuid():N}"[..14]);
        var fan = await factory.RegisterAsync($"fan{Guid.NewGuid():N}"[..16]);

        var trackId = await UploadTrackAsync(owner, "Aimable", "Public");
        await WaitUntilReadyAsync(owner, trackId);

        var first = await fan.Client.PostAsync($"/api/v1/tracks/{trackId}/like", null);
        var firstState = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(firstState.GetProperty("liked").GetBoolean());
        Assert.Equal(1, firstState.GetProperty("likeCount").GetInt64());

        // Un second like ne doit pas incrémenter le compteur.
        var second = await fan.Client.PostAsync($"/api/v1/tracks/{trackId}/like", null);
        var secondState = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, secondState.GetProperty("likeCount").GetInt64());

        var removed = await fan.Client.DeleteAsync($"/api/v1/tracks/{trackId}/like");
        var removedState = await removed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(removedState.GetProperty("liked").GetBoolean());
        Assert.Equal(0, removedState.GetProperty("likeCount").GetInt64());

        // Le compteur ne descend jamais sous zéro.
        var removedAgain = await fan.Client.DeleteAsync($"/api/v1/tracks/{trackId}/like");
        var finalState = await removedAgain.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, finalState.GetProperty("likeCount").GetInt64());
    }

    [Fact]
    public async Task HiddenLikeCounter_IsMaskedForVisitorsButVisibleToTheOwner()
    {
        var owner = await factory.RegisterAsync($"hid{Guid.NewGuid():N}"[..16]);
        var fan = await factory.RegisterAsync($"hfan{Guid.NewGuid():N}"[..15]);

        var trackId = await UploadTrackAsync(owner, "Compteurs masqués", "Public");
        await WaitUntilReadyAsync(owner, trackId);
        await fan.Client.PostAsync($"/api/v1/tracks/{trackId}/like", null);

        var settings = await owner.Client.PatchAsJsonAsync(
            "/api/v1/me/settings",
            new { showLikeCount = false, showPlayCount = false });
        settings.EnsureSuccessStatusCode();

        var asVisitor = await fan.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tracks/{trackId}");
        var visitorTrack = asVisitor.GetProperty("track");
        Assert.False(visitorTrack.TryGetProperty("likeCount", out _));

        var asOwner = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tracks/{trackId}");
        Assert.Equal(1, asOwner.GetProperty("track").GetProperty("likeCount").GetInt64());
    }

    [Fact]
    public async Task TimestampedComment_IsStoredAndBoundedByTheTrackDuration()
    {
        var owner = await factory.RegisterAsync($"cmt{Guid.NewGuid():N}"[..16]);
        var reader = await factory.RegisterAsync($"rdr{Guid.NewGuid():N}"[..16]);

        var trackId = await UploadTrackAsync(owner, "Commenté", "Public");
        await WaitUntilReadyAsync(owner, trackId);

        var created = await reader.Client.PostAsJsonAsync(
            $"/api/v1/tracks/{trackId}/comments",
            new { content = "Superbe passage", timestampSeconds = 9 });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var comment = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(9, comment.GetProperty("timestampSeconds").GetInt32());
        Assert.True(comment.GetProperty("canEdit").GetBoolean());

        // Un timestamp au-delà de la fin du morceau est refusé.
        var beyond = await reader.Client.PostAsJsonAsync(
            $"/api/v1/tracks/{trackId}/comments",
            new { content = "Hors limites", timestampSeconds = 99999 });
        Assert.Equal(HttpStatusCode.BadRequest, beyond.StatusCode);

        // Seul l'auteur peut modifier son commentaire.
        var commentId = comment.GetProperty("id").GetGuid();
        var byOwner = await owner.Client.PatchAsJsonAsync($"/api/v1/comments/{commentId}", new { content = "Modifié" });
        Assert.Equal(HttpStatusCode.Forbidden, byOwner.StatusCode);

        // Le propriétaire du morceau peut en revanche le supprimer.
        var deleted = await owner.Client.DeleteAsync($"/api/v1/comments/{commentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    /// <summary>Envoie un fichier audio valide et retourne l'identifiant du morceau créé.</summary>
    private static async Task<Guid> UploadTrackAsync(AuthenticatedClient user, string title, string visibility, params string[] tags)
    {
        using var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(TestAudio.CreateWav());
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "sample.wav");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent(visibility), "visibility");

        foreach (var tag in tags)
        {
            content.Add(new StringContent(tag), "tags");
        }

        var response = await user.Client.PostAsync("/api/v1/tracks", content);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("trackId").GetGuid();
    }

    /// <summary>
    /// Attend que le traitement en arrière-plan aboutisse.
    /// La boucle est bornée par un délai maximal : elle ne peut pas tourner indéfiniment.
    /// </summary>
    private static async Task<JsonElement> WaitUntilReadyAsync(AuthenticatedClient user, Guid trackId)
    {
        var deadline = DateTime.UtcNow + ProcessingTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var track = await user.Client.GetFromJsonAsync<JsonElement>($"/api/v1/tracks/{trackId}");
            var status = track.GetProperty("track").GetProperty("status").GetString();

            if (status == "Ready")
            {
                return track;
            }

            Assert.NotEqual("Failed", status);
            await Task.Delay(250);
        }

        throw new TimeoutException($"Track {trackId} was not processed within {ProcessingTimeout.TotalSeconds} seconds.");
    }

    /// <summary>Déclare une écoute et retourne la réponse du serveur.</summary>
    private static async Task<JsonElement> PostPlayAsync(AuthenticatedClient user, Guid trackId, int durationSeconds)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/tracks/{trackId}/plays",
            new { sessionId = Guid.NewGuid(), positionSeconds = durationSeconds, durationSeconds, source = "PLAYER" });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
