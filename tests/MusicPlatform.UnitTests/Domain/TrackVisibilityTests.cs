using MusicPlatform.Domain.Entities;
using MusicPlatform.Domain.Enums;
using MusicPlatform.Domain.Exceptions;

namespace MusicPlatform.UnitTests.Domain;

/// <summary>
/// Règles de visibilité et de publication d'un morceau.
/// Ce sont les règles les plus sensibles du domaine : elles décident qui peut
/// consulter et écouter un contenu.
/// </summary>
public sealed class TrackVisibilityTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();

    /// <summary>Construit un morceau prêt à l'écoute avec la visibilité demandée.</summary>
    private static Track ReadyTrack(ContentVisibility visibility) => new()
    {
        OwnerId = OwnerId,
        Title = "Titre",
        ArtistName = "Artiste",
        DurationSeconds = 180,
        Status = TrackStatus.Ready,
        Visibility = visibility,
    };

    [Fact]
    public void PublicReadyTrack_IsAccessibleByAnonymousVisitor()
    {
        var track = ReadyTrack(ContentVisibility.Public);

        Assert.True(track.IsAccessibleBy(null, UserRole.User));
        Assert.True(track.IsPubliclyListed);
    }

    [Fact]
    public void UnlistedTrack_IsAccessibleByLinkButNotPubliclyListed()
    {
        var track = ReadyTrack(ContentVisibility.Unlisted);

        Assert.True(track.IsAccessibleBy(StrangerId, UserRole.User));
        Assert.False(track.IsPubliclyListed);
    }

    [Fact]
    public void PrivateTrack_IsHiddenFromEveryoneButOwnerAndModeration()
    {
        var track = ReadyTrack(ContentVisibility.Private);

        Assert.False(track.IsAccessibleBy(null, UserRole.User));
        Assert.False(track.IsAccessibleBy(StrangerId, UserRole.User));
        Assert.True(track.IsAccessibleBy(OwnerId, UserRole.User));
        Assert.True(track.IsAccessibleBy(StrangerId, UserRole.Moderator));
        Assert.True(track.IsAccessibleBy(StrangerId, UserRole.Admin));
    }

    [Fact]
    public void HiddenTrack_IsNoLongerAccessibleToThePublic()
    {
        var track = ReadyTrack(ContentVisibility.Public);
        track.HiddenAt = DateTime.UtcNow;

        Assert.False(track.IsAccessibleBy(StrangerId, UserRole.User));
        Assert.False(track.IsPubliclyListed);

        // Le propriétaire garde l'accès pour comprendre pourquoi son morceau a disparu.
        Assert.True(track.IsAccessibleBy(OwnerId, UserRole.User));
    }

    [Fact]
    public void DeletedTrack_IsAccessibleToNobody()
    {
        var track = ReadyTrack(ContentVisibility.Public);
        track.DeletedAt = DateTime.UtcNow;

        Assert.False(track.IsAccessibleBy(OwnerId, UserRole.User));
        Assert.False(track.IsAccessibleBy(StrangerId, UserRole.Admin));
    }

    [Theory]
    [InlineData(TrackStatus.Uploading)]
    [InlineData(TrackStatus.Processing)]
    [InlineData(TrackStatus.Failed)]
    public void TrackStillProcessing_IsNotAccessibleToThePublic(TrackStatus status)
    {
        var track = ReadyTrack(ContentVisibility.Public);
        track.Status = status;

        Assert.False(track.IsAccessibleBy(StrangerId, UserRole.User));
        Assert.False(track.IsPlayable);
    }

    [Fact]
    public void OnlyOwnerAndAdministratorCanManageATrack()
    {
        var track = ReadyTrack(ContentVisibility.Public);

        Assert.True(track.IsManageableBy(OwnerId, UserRole.User));
        Assert.True(track.IsManageableBy(StrangerId, UserRole.Admin));
        Assert.False(track.IsManageableBy(StrangerId, UserRole.User));

        // Un modérateur peut masquer un contenu mais pas modifier ses métadonnées.
        Assert.False(track.IsManageableBy(StrangerId, UserRole.Moderator));
        Assert.False(track.IsManageableBy(null, UserRole.User));
    }

    [Fact]
    public void PublishingBeforeProcessingCompletes_IsRejected()
    {
        var track = ReadyTrack(ContentVisibility.Private);
        track.Status = TrackStatus.Processing;

        var exception = Assert.Throws<DomainException>(() => track.Publish(DateTime.UtcNow));
        Assert.Equal("TRACK_NOT_READY", exception.Code);
    }

    [Fact]
    public void PublishingAReadyTrack_MakesItPublicAndStampsPublicationDate()
    {
        var track = ReadyTrack(ContentVisibility.Private);
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        track.Publish(now);

        Assert.Equal(ContentVisibility.Public, track.Visibility);
        Assert.Equal(now, track.PublishedAt);
    }

    [Fact]
    public void PublishingTwice_KeepsTheOriginalPublicationDate()
    {
        var track = ReadyTrack(ContentVisibility.Private);
        var first = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        track.Publish(first);
        track.Unpublish(first.AddDays(1));
        track.Publish(first.AddDays(2));

        Assert.Equal(first, track.PublishedAt);
    }

    [Fact]
    public void MarkReady_RejectsAnInvalidDuration()
    {
        var track = ReadyTrack(ContentVisibility.Private);
        track.Status = TrackStatus.Processing;

        var exception = Assert.Throws<DomainException>(() => track.MarkReady(0, DateTime.UtcNow));
        Assert.Equal("TRACK_UPLOAD_INVALID", exception.Code);
    }

    [Fact]
    public void MarkReady_ClearsAPreviousFailureReason()
    {
        var track = ReadyTrack(ContentVisibility.Private);
        track.MarkFailed("fichier illisible", DateTime.UtcNow);

        track.MarkReady(240, DateTime.UtcNow);

        Assert.Equal(TrackStatus.Ready, track.Status);
        Assert.Equal(240, track.DurationSeconds);
        Assert.Null(track.FailureReason);
    }
}
