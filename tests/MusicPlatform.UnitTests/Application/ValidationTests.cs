using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Analytics;
using MusicPlatform.Application.Features.Tracks;
using MusicPlatform.Domain.Entities;

namespace MusicPlatform.UnitTests.Application;

/// <summary>Validation des fichiers audio : extension, taille et signature binaire.</summary>
public sealed class AudioFileValidatorTests
{
    [Theory]
    [InlineData("track.mp3")]
    [InlineData("track.MP3")]
    [InlineData("track.flac")]
    [InlineData("track.wav")]
    [InlineData("track.ogg")]
    [InlineData("track.m4a")]
    [InlineData("track.opus")]
    public void AcceptsSupportedExtensions(string fileName)
    {
        var extension = AudioFileValidator.ValidateNameAndSize(fileName, 1024);

        Assert.Equal(Path.GetExtension(fileName).ToLowerInvariant(), extension);
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("archive.zip")]
    [InlineData("video.mp4")]
    [InlineData("noextension")]
    public void RejectsUnsupportedExtensions(string fileName)
    {
        Assert.Throws<UnsupportedMediaTypeException>(() => AudioFileValidator.ValidateNameAndSize(fileName, 1024));
    }

    [Fact]
    public void RejectsAFileLargerThanTwentyMegabytes()
    {
        Assert.Throws<PayloadTooLargeException>(() =>
            AudioFileValidator.ValidateNameAndSize("track.mp3", Track.MaxAudioFileSizeBytes + 1));
    }

    [Fact]
    public void AcceptsAFileExactlyAtTheSizeLimit()
    {
        var extension = AudioFileValidator.ValidateNameAndSize("track.mp3", Track.MaxAudioFileSizeBytes);

        Assert.Equal(".mp3", extension);
    }

    [Fact]
    public void RejectsAnEmptyFile()
    {
        Assert.Throws<InputValidationException>(() => AudioFileValidator.ValidateNameAndSize("track.mp3", 0));
    }

    [Theory]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04, 0, 0, 0, 0, 0, 0, 0, 0 })] // ID3v2
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0 })] // trame MPEG
    [InlineData(new byte[] { 0x66, 0x4C, 0x61, 0x43, 0, 0, 0, 0, 0, 0, 0, 0 })] // fLaC
    [InlineData(new byte[] { 0x4F, 0x67, 0x67, 0x53, 0, 0, 0, 0, 0, 0, 0, 0 })] // OggS
    public void RecognisesKnownAudioSignatures(byte[] header)
    {
        Assert.True(AudioFileValidator.HasKnownAudioSignature(header));
    }

    [Fact]
    public void RecognisesARiffWaveContainer()
    {
        byte[] header = [0x52, 0x49, 0x46, 0x46, 0x24, 0, 0, 0, 0x57, 0x41, 0x56, 0x45];

        Assert.True(AudioFileValidator.HasKnownAudioSignature(header));
    }

    [Fact]
    public void RecognisesAnIsoBaseMediaContainer()
    {
        byte[] header = [0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20];

        Assert.True(AudioFileValidator.HasKnownAudioSignature(header));
    }

    [Fact]
    public void RejectsAContentThatIsNotAudio()
    {
        // En-tête d'un exécutable Windows renommé en .mp3.
        byte[] header = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0, 0x04, 0, 0, 0];

        Assert.False(AudioFileValidator.HasKnownAudioSignature(header));
        Assert.Throws<UnprocessableException>(() => AudioFileValidator.EnsureAudioSignature(header));
    }

    [Fact]
    public void RejectsATruncatedHeader()
    {
        Assert.False(AudioFileValidator.HasKnownAudioSignature([0x49, 0x44]));
    }
}

/// <summary>Normalisation des tags saisis par les utilisateurs.</summary>
public sealed class TagNormalizationTests
{
    [Theory]
    [InlineData("#Rock", "rock")]
    [InlineData("Rock", "rock")]
    [InlineData("  #Indie Rock  ", "indie-rock")]
    [InlineData("Électro", "electro")]
    [InlineData("Drum & Bass", "drum-bass")]
    [InlineData("HIP-HOP", "hip-hop")]
    [InlineData("lo—fi", "lo-fi")]
    [InlineData("###", "")]
    [InlineData("   ", "")]
    public void NormalizesLabelsToStableSlugs(string input, string expected)
    {
        Assert.Equal(expected, Tag.Normalize(input));
    }

    [Fact]
    public void CollapsesConsecutiveSeparators()
    {
        Assert.Equal("indie-rock", Tag.Normalize("indie   ///   rock"));
    }

    [Fact]
    public void DeduplicatesAndCapsTheNumberOfTags()
    {
        var raw = Enumerable.Range(0, 40).Select(i => $"#tag{i}").Concat(["#tag0", "#TAG0"]).ToList();

        var slugs = TagResolver.NormalizeAll(raw);

        Assert.Equal(TagResolver.MaxTagsPerTrack, slugs.Count);
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
    }

    [Fact]
    public void DropsEmptyLabels()
    {
        var slugs = TagResolver.NormalizeAll(["#rock", "   ", "###", "jazz"]);

        Assert.Equal(["rock", "jazz"], slugs);
    }

    [Fact]
    public void RejectsATagThatIsTooLong()
    {
        var raw = new List<string> { new('a', TagResolver.MaxTagLength + 1) };

        Assert.Throws<InputValidationException>(() => TagResolver.NormalizeAll(raw));
    }
}

/// <summary>Bornes de pagination et échappement des motifs de recherche.</summary>
public sealed class PagingAndPatternTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void PageNumberIsAlwaysAtLeastOne(int requested, int expected)
    {
        Assert.Equal(expected, new PageRequest { Page = requested }.Page);
    }

    [Theory]
    [InlineData(0, PageRequest.DefaultPageSize)]
    [InlineData(-1, PageRequest.DefaultPageSize)]
    [InlineData(50, 50)]
    [InlineData(5000, PageRequest.MaxPageSize)]
    public void PageSizeIsBoundedToProtectTheDatabase(int requested, int expected)
    {
        Assert.Equal(expected, new PageRequest { PageSize = requested }.PageSize);
    }

    [Fact]
    public void SkipReflectsTheRequestedPage()
    {
        Assert.Equal(40, new PageRequest { Page = 3, PageSize = 20 }.Skip);
        Assert.Equal(0, new PageRequest { Page = 1, PageSize = 20 }.Skip);
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(150, 20, 8)]
    public void TotalPagesRoundsUp(long totalItems, int pageSize, int expected)
    {
        var page = new PagedResult<string> { Items = [], Page = 1, PageSize = pageSize, TotalItems = totalItems };

        Assert.Equal(expected, page.TotalPages);
    }

    [Fact]
    public void MapPreservesPaginationMetadata()
    {
        var source = new PagedResult<int> { Items = [1, 2, 3], Page = 2, PageSize = 3, TotalItems = 9 };

        var mapped = source.Map(value => value.ToString());

        Assert.Equal(["1", "2", "3"], mapped.Items);
        Assert.Equal(2, mapped.Page);
        Assert.Equal(9, mapped.TotalItems);
        Assert.Equal(3, mapped.TotalPages);
    }

    [Fact]
    public void SearchPatternEscapesWildcardsTypedByUsers()
    {
        // Sans échappement, « 100% » correspondrait à l'ensemble du catalogue.
        Assert.Equal(@"%100\%%", SqlPatterns.Contains("100%"));
        Assert.Equal(@"%a\_b%", SqlPatterns.Contains("A_b"));
        Assert.Equal("%rock%", SqlPatterns.Contains("  Rock  "));
    }
}

/// <summary>Agrégation des séries temporelles de statistiques.</summary>
public sealed class AnalyticsAggregationTests
{
    /// <summary>Construit une série journalière continue à partir d'une date de départ.</summary>
    private static List<PlaysPointDto> DailySeries(DateOnly start, params long[] plays) =>
        plays.Select((value, index) => new PlaysPointDto(start.AddDays(index), value, 1)).ToList();

    [Fact]
    public void DailyGroupingReturnsTheSeriesUnchanged()
    {
        var daily = DailySeries(new DateOnly(2026, 3, 2), 1, 2, 3);

        var grouped = AnalyticsService.GroupPoints(daily, AnalyticsGroupBy.Day);

        Assert.Equal(3, grouped.Count);
    }

    [Fact]
    public void WeeklyGroupingSumsFromMonday()
    {
        // Le 2 mars 2026 est un lundi : sept jours forment donc une seule semaine.
        var daily = DailySeries(new DateOnly(2026, 3, 2), 1, 1, 1, 1, 1, 1, 1);

        var grouped = AnalyticsService.GroupPoints(daily, AnalyticsGroupBy.Week);

        Assert.Single(grouped);
        Assert.Equal(7, grouped[0].Plays);
        Assert.Equal(new DateOnly(2026, 3, 2), grouped[0].Date);
    }

    [Fact]
    public void WeeklyGroupingSplitsAcrossWeekBoundaries()
    {
        var daily = DailySeries(new DateOnly(2026, 3, 2), 1, 1, 1, 1, 1, 1, 1, 5);

        var grouped = AnalyticsService.GroupPoints(daily, AnalyticsGroupBy.Week);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(7, grouped[0].Plays);
        Assert.Equal(5, grouped[1].Plays);
    }

    [Fact]
    public void MonthlyGroupingSumsPerCalendarMonth()
    {
        var daily = new List<PlaysPointDto>
        {
            new(new DateOnly(2026, 1, 5), 2, 1),
            new(new DateOnly(2026, 1, 20), 3, 1),
            new(new DateOnly(2026, 2, 1), 4, 1),
        };

        var grouped = AnalyticsService.GroupPoints(daily, AnalyticsGroupBy.Month);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(5, grouped[0].Plays);
        Assert.Equal(4, grouped[1].Plays);
    }

    [Fact]
    public void EmptySeriesRemainsEmpty()
    {
        Assert.Empty(AnalyticsService.GroupPoints([], AnalyticsGroupBy.Week));
    }

    [Fact]
    public void RangeDefaultsToTheLastThirtyDays()
    {
        var (start, end) = AnalyticsService.NormalizeRange(null, null);

        Assert.Equal(30, (end - start).Days);
    }

    [Fact]
    public void RangeRejectsAnInvertedPeriod()
    {
        var from = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Throws<InputValidationException>(() => AnalyticsService.NormalizeRange(from, to));
    }

    [Fact]
    public void RangeRejectsAPeriodLongerThanAYear()
    {
        var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Throws<InputValidationException>(() => AnalyticsService.NormalizeRange(from, to));
    }
}
