using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;
using MusicPlatform.Domain.Exceptions;

namespace MusicPlatform.UnitTests.Domain;

/// <summary>Règles de réordonnancement et de visibilité des playlists.</summary>
public sealed class PlaylistTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();

    /// <summary>Construit une playlist contenant <paramref name="count"/> morceaux ordonnés.</summary>
    private static (Playlist Playlist, List<Guid> TrackIds) BuildPlaylist(int count)
    {
        var playlist = new Playlist { OwnerId = OwnerId, Name = "Ma playlist" };
        var ids = new List<Guid>();

        for (var i = 0; i < count; i++)
        {
            var trackId = Guid.NewGuid();
            ids.Add(trackId);
            playlist.Items.Add(new PlaylistItem { PlaylistId = playlist.Id, TrackId = trackId, Position = i });
        }

        return (playlist, ids);
    }

    [Fact]
    public void Reorder_AppliesTheRequestedPositions()
    {
        var (playlist, ids) = BuildPlaylist(3);

        playlist.Reorder(
            new Dictionary<Guid, int> { [ids[0]] = 2, [ids[1]] = 0, [ids[2]] = 1 },
            DateTime.UtcNow);

        Assert.Equal(2, playlist.Items.First(i => i.TrackId == ids[0]).Position);
        Assert.Equal(0, playlist.Items.First(i => i.TrackId == ids[1]).Position);
        Assert.Equal(1, playlist.Items.First(i => i.TrackId == ids[2]).Position);
    }

    [Fact]
    public void Reorder_RefusesAnIncompleteRequest()
    {
        var (playlist, ids) = BuildPlaylist(3);

        var exception = Assert.Throws<DomainException>(() =>
            playlist.Reorder(new Dictionary<Guid, int> { [ids[0]] = 0 }, DateTime.UtcNow));

        Assert.Equal("PLAYLIST_REORDER_INVALID", exception.Code);
    }

    [Fact]
    public void Reorder_RefusesDuplicatePositions()
    {
        var (playlist, ids) = BuildPlaylist(3);

        Assert.Throws<DomainException>(() =>
            playlist.Reorder(
                new Dictionary<Guid, int> { [ids[0]] = 0, [ids[1]] = 0, [ids[2]] = 1 },
                DateTime.UtcNow));
    }

    [Fact]
    public void Reorder_RefusesNonContiguousPositions()
    {
        var (playlist, ids) = BuildPlaylist(3);

        Assert.Throws<DomainException>(() =>
            playlist.Reorder(
                new Dictionary<Guid, int> { [ids[0]] = 0, [ids[1]] = 1, [ids[2]] = 5 },
                DateTime.UtcNow));
    }

    [Fact]
    public void Reorder_RefusesATrackThatIsNotInThePlaylist()
    {
        var (playlist, ids) = BuildPlaylist(2);

        Assert.Throws<DomainException>(() =>
            playlist.Reorder(
                new Dictionary<Guid, int> { [ids[0]] = 0, [Guid.NewGuid()] = 1 },
                DateTime.UtcNow));
    }

    [Fact]
    public void Reorder_LeavesThePlaylistUnchangedWhenItFails()
    {
        var (playlist, ids) = BuildPlaylist(3);
        var before = playlist.Items.ToDictionary(i => i.TrackId, i => i.Position);

        Assert.Throws<DomainException>(() =>
            playlist.Reorder(new Dictionary<Guid, int> { [ids[0]] = 0 }, DateTime.UtcNow));

        foreach (var item in playlist.Items)
        {
            Assert.Equal(before[item.TrackId], item.Position);
        }
    }

    [Fact]
    public void PrivatePlaylist_IsVisibleOnlyToItsOwnerAndModeration()
    {
        var playlist = new Playlist { OwnerId = OwnerId, Visibility = ContentVisibility.Private };

        Assert.True(playlist.IsAccessibleBy(OwnerId, UserRole.User));
        Assert.False(playlist.IsAccessibleBy(StrangerId, UserRole.User));
        Assert.False(playlist.IsAccessibleBy(null, UserRole.User));
        Assert.True(playlist.IsAccessibleBy(StrangerId, UserRole.Moderator));
    }

    [Fact]
    public void UnlistedPlaylist_IsReachableByLink()
    {
        var playlist = new Playlist { OwnerId = OwnerId, Visibility = ContentVisibility.Unlisted };

        Assert.True(playlist.IsAccessibleBy(StrangerId, UserRole.User));
        Assert.True(playlist.IsAccessibleBy(null, UserRole.User));
    }

    [Fact]
    public void OnlyOwnerAndAdministratorCanEditAPlaylist()
    {
        var playlist = new Playlist { OwnerId = OwnerId };

        Assert.True(playlist.IsManageableBy(OwnerId, UserRole.User));
        Assert.True(playlist.IsManageableBy(StrangerId, UserRole.Admin));
        Assert.False(playlist.IsManageableBy(StrangerId, UserRole.Moderator));
        Assert.False(playlist.IsManageableBy(null, UserRole.User));
    }
}

/// <summary>Règles sociales : abonnements et permissions sur les commentaires.</summary>
public sealed class SocialRulesTests
{
    [Fact]
    public void Follow_RefusesSelfSubscription()
    {
        var userId = Guid.NewGuid();

        var exception = Assert.Throws<DomainException>(() => Follow.Create(userId, userId));
        Assert.Equal("FOLLOW_SELF_NOT_ALLOWED", exception.Code);
    }

    [Fact]
    public void Follow_IsCreatedBetweenTwoDistinctUsers()
    {
        var follower = Guid.NewGuid();
        var followed = Guid.NewGuid();

        var follow = Follow.Create(follower, followed);

        Assert.Equal(follower, follow.FollowerId);
        Assert.Equal(followed, follow.FollowedId);
    }

    [Fact]
    public void OnlyTheAuthorCanEditTheirComment()
    {
        var author = Guid.NewGuid();
        var comment = new Comment { AuthorId = author, Content = "Bien joué" };

        Assert.True(comment.IsEditableBy(author));
        Assert.False(comment.IsEditableBy(Guid.NewGuid()));
    }

    [Fact]
    public void AuthorOwnerAndModerationCanDeleteAComment()
    {
        var author = Guid.NewGuid();
        var trackOwner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var comment = new Comment { AuthorId = author, Content = "Bien joué" };

        Assert.True(comment.IsDeletableBy(author, UserRole.User, trackOwner));
        Assert.True(comment.IsDeletableBy(trackOwner, UserRole.User, trackOwner));
        Assert.True(comment.IsDeletableBy(stranger, UserRole.Moderator, trackOwner));
        Assert.False(comment.IsDeletableBy(stranger, UserRole.User, trackOwner));
    }

    [Fact]
    public void ADeletedCommentCanNoLongerBeEditedOrDeleted()
    {
        var author = Guid.NewGuid();
        var comment = new Comment { AuthorId = author, DeletedAt = DateTime.UtcNow };

        Assert.False(comment.IsEditableBy(author));
        Assert.False(comment.IsDeletableBy(author, UserRole.Admin, author));
    }

    [Theory]
    [InlineData(UserStatus.Active, null, true)]
    [InlineData(UserStatus.Suspended, null, false)]
    public void SuspendedOrDeletedAccounts_AreNotActive(UserStatus status, DateTime? deletedAt, bool expected)
    {
        var user = new User { Status = status, DeletedAt = deletedAt };

        Assert.Equal(expected, user.IsActive);
    }

    [Fact]
    public void DeletedAccount_IsNeverActive()
    {
        var user = new User { Status = UserStatus.Active, DeletedAt = DateTime.UtcNow };

        Assert.False(user.IsActive);
    }

    [Fact]
    public void PrivateProfile_IsVisibleOnlyToItsOwnerAndModeration()
    {
        var user = new User { ProfileVisibility = ProfileVisibility.Private };

        Assert.True(user.IsProfileVisibleTo(user.Id, UserRole.User));
        Assert.False(user.IsProfileVisibleTo(Guid.NewGuid(), UserRole.User));
        Assert.True(user.IsProfileVisibleTo(Guid.NewGuid(), UserRole.Admin));
    }

    [Fact]
    public void ExportIsDownloadableOnlyWhenReadyAndNotExpired()
    {
        var now = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        var ready = new UserExport { Status = UserExportStatus.Ready, StoragePath = "exports/a.zip", ExpiresAt = now.AddDays(1) };
        var expired = new UserExport { Status = UserExportStatus.Ready, StoragePath = "exports/b.zip", ExpiresAt = now.AddDays(-1) };
        var pending = new UserExport { Status = UserExportStatus.Pending };
        var withoutFile = new UserExport { Status = UserExportStatus.Ready, StoragePath = null };

        Assert.True(ready.IsDownloadable(now));
        Assert.False(expired.IsDownloadable(now));
        Assert.False(pending.IsDownloadable(now));
        Assert.False(withoutFile.IsDownloadable(now));
    }

    [Fact]
    public void RefreshTokenIsUsableOnlyWhileValidAndNotRevoked()
    {
        var now = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(new RefreshToken { ExpiresAt = now.AddDays(1) }.IsUsable(now));
        Assert.False(new RefreshToken { ExpiresAt = now.AddDays(-1) }.IsUsable(now));
        Assert.False(new RefreshToken { ExpiresAt = now.AddDays(1), RevokedAt = now }.IsUsable(now));
    }
}
