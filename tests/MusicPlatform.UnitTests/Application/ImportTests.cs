using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicPlatform.Application.Features.Import;
using MusicPlatform.Infrastructure.Media;
using MusicPlatform.Infrastructure.Providers;

namespace MusicPlatform.UnitTests.Application;

/// <summary>
/// Normalisation des métadonnées : c'est elle qui décide si deux libellés issus de
/// plateformes différentes désignent le même enregistrement.
/// </summary>
public sealed class MetadataNormalizerTests
{
    [Theory]
    [InlineData("Bohemian Rhapsody", "bohemian rhapsody")]
    [InlineData("BOHEMIAN RHAPSODY", "bohemian rhapsody")]
    [InlineData("Bohemian  Rhapsody ", "bohemian rhapsody")]
    // La ponctuation devient une espace plutôt que d'être supprimée : « Rock&Roll » et
    // « Rock & Roll » se rejoignent ainsi sur la même forme.
    [InlineData("L'Été indien", "l ete indien")]
    [InlineData("Où es-tu ?", "ou es tu")]
    [InlineData("Rock&Roll", "rock roll")]
    public void ReducesALabelToItsComparableForm(string value, string expected)
    {
        Assert.Equal(expected, MetadataNormalizer.Normalize(value));
    }

    [Theory]
    [InlineData("Song Title (Official Video)")]
    [InlineData("Song Title [Official Audio]")]
    [InlineData("Song Title (Lyrics)")]
    [InlineData("Song Title (Remastered)")]
    [InlineData("Song Title (HD)")]
    public void RemovesEditorialDecorations(string value)
    {
        Assert.Equal("song title", MetadataNormalizer.Normalize(value));
    }

    [Fact]
    public void KeepsMeaningfulParentheses()
    {
        // « Live at Wembley » distingue un enregistrement, contrairement à « Official Video ».
        Assert.Equal("song title live at wembley", MetadataNormalizer.Normalize("Song Title (Live at Wembley)"));
    }

    [Theory]
    [InlineData("Song Title feat. Someone", "song title")]
    [InlineData("Song Title ft. Someone", "song title")]
    [InlineData("Song Title featuring Someone", "song title")]
    public void DropsTheGuestArtistMention(string value, string expected)
    {
        Assert.Equal(expected, MetadataNormalizer.Normalize(value));
    }

    [Theory]
    [InlineData("Daft Punk", "Daft Punk")]
    [InlineData("Daft Punk, Pharrell Williams", "Daft Punk")]
    [InlineData("Daft Punk & Pharrell Williams", "Daft Punk")]
    [InlineData("Daft Punk feat. Pharrell Williams", "Daft Punk")]
    public void KeepsOnlyThePrimaryArtist(string credit, string expected)
    {
        Assert.Equal(expected, MetadataNormalizer.PrimaryArtist(credit));
    }

    [Fact]
    public void BuildsTheSameKeyForVariantsOfOneRecording()
    {
        // Trois écritures du même enregistrement, telles que les plateformes les exposent.
        var fromSpotify = MetadataNormalizer.BuildMatchKey("Daft Punk, Pharrell Williams", "Get Lucky");
        var fromYoutube = MetadataNormalizer.BuildMatchKey("Daft Punk", "Get Lucky (Official Video)");
        var fromDeezer = MetadataNormalizer.BuildMatchKey("Daft Punk", "Get Lucky feat. Pharrell Williams");

        Assert.Equal(fromSpotify, fromYoutube);
        Assert.Equal(fromYoutube, fromDeezer);
    }

    [Fact]
    public void DistinguishesDifferentRecordings()
    {
        Assert.NotEqual(
            MetadataNormalizer.BuildMatchKey("Daft Punk", "Get Lucky"),
            MetadataNormalizer.BuildMatchKey("Daft Punk", "Instant Crush"));
    }

    [Fact]
    public void ReturnsNoKeyWithoutAnyMetadata()
    {
        Assert.Null(MetadataNormalizer.BuildMatchKey(null, null));
        Assert.Null(MetadataNormalizer.BuildMatchKey("  ", "  "));
    }

    [Theory]
    [InlineData("Artist - Title", "Artist", "Title")]
    [InlineData("Artist – Title", "Artist", "Title")]
    [InlineData("Artist | Title", "Artist", "Title")]
    public void SplitsTheUsualVideoTitleForms(string videoTitle, string artist, string title)
    {
        var (parsedArtist, parsedTitle) = MetadataNormalizer.SplitVideoTitle(videoTitle, "Channel");

        Assert.Equal(artist, parsedArtist);
        Assert.Equal(title, parsedTitle);
    }

    [Fact]
    public void FallsBackToTheChannelWhenTheTitleHasNoSeparator()
    {
        var (artist, title) = MetadataNormalizer.SplitVideoTitle("Just A Title", "Channel");

        Assert.Equal("Channel", artist);
        Assert.Equal("Just A Title", title);
    }

    [Theory]
    [InlineData(180, 180, true)]
    [InlineData(180, 184, true)]
    [InlineData(180, 200, false)]
    [InlineData(0, 180, false)]
    public void ComparesDurationsWithinTolerance(int left, int right, bool expected)
    {
        Assert.Equal(expected, MetadataNormalizer.DurationsMatch(left, right));
    }
}

/// <summary>
/// Analyse des liens de playlist YouTube. Le contrôle du domaine est ce qui empêche
/// qu'une adresse arbitraire soit transmise à yt-dlp.
/// </summary>
public sealed class PlaylistLinkParsingTests
{
    private static YoutubePlaylistProvider Youtube() =>
        new(
            new YtDlpProcessRunner(Options.Create(new YtDlpOptions()), NullLogger<YtDlpProcessRunner>.Instance),
            NullLogger<YoutubePlaylistProvider>.Instance);

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabcdefghijkl")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&list=PLabcdefghijkl")]
    [InlineData("https://music.youtube.com/playlist?list=PLabcdefghijkl")]
    [InlineData("https://m.youtube.com/playlist?list=PLabcdefghijkl")]
    [InlineData("PLabcdefghijkl")]
    public void RecognisesYoutubePlaylists(string link)
    {
        Assert.Equal("PLabcdefghijkl", Youtube().TryParsePlaylistId(link));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas une url")]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://www.deezer.com/playlist/1234567890")]
    [InlineData("https://youtube.com.attaquant.example/playlist?list=PLabcdefghijkl")]
    // Une vidéo isolée n'est pas une playlist : elle relève de l'import d'un morceau.
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ")]
    public void RejectsLinksThatAreNotYoutubePlaylists(string link)
    {
        Assert.Null(Youtube().TryParsePlaylistId(link));
    }
}
