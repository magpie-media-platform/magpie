using Magpie.Extensions.Tv.Parsing;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tv.Parsing;

[TestFixture]
public class TvReleaseTitleParsingExtensionsTests
{
    [Test]
    public void Parse_RejectsEndEpisode_WhenResolutionFollowsNumXNumToken()
    {
        // Arrange
        var title = "show 1x05 720p";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Seasons, Is.EqualTo([1]));
            Assert.That(parsed.Episodes, Is.EqualTo([5]));
        }
    }

    [Test]
    public void Parse_ParsesEpisodeRange_WhenNumXNumCarriesExplicitEnd()
    {
        // Arrange
        var title = "show 1x05-1x08";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Seasons, Is.EqualTo([1]));
            Assert.That(parsed.Episodes, Is.EqualTo([5, 6, 7, 8]));
        }
    }

    [Test]
    public void Parse_RejectsEndEpisode_WhenYearFollowsSeasonEpisodeToken()
    {
        // Arrange
        var title = "mash s03e05 1974 dvdrip";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Seasons, Is.EqualTo([3]));
            Assert.That(parsed.Episodes, Is.EqualTo([5]));
        }
    }

    [Test]
    public void Parse_ParsesEpisodeRange_WhenSeasonEpisodeCarriesExplicitEnd()
    {
        // Arrange
        var title = "show s01e01-e04";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Seasons, Is.EqualTo([1]));
            Assert.That(parsed.Episodes, Is.EqualTo([1, 2, 3, 4]));
        }
    }

    [Test]
    public void Parse_RejectsEndSeason_WhenAudioChannelsFollowTerseSeasonToken()
    {
        // Arrange
        var title = "Show.S01.5.1.AAC";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Seasons, Is.EqualTo([1]));
            Assert.That(parsed.Episodes, Is.Empty);
        }
    }

    [Test]
    public void Parse_ExpandsSeasonRange_WhenDashedSeasonRangeIsPresent()
    {
        // Arrange
        var title = "Breaking.Bad.S01-S05.1080p.BluRay.x264";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        Assert.That(parsed.Seasons, Is.EqualTo([1, 2, 3, 4, 5]));
    }

    [Test]
    public void Parse_ListsSeasonsWithoutGapFill_WhenSpaceSeparatedSeasonTokensArePresent()
    {
        // Arrange
        var title = "Breaking Bad s01 s05";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        Assert.That(parsed.Seasons, Is.EqualTo([1, 5]));
    }

    [Test]
    public void Parse_ListsSeasonsWithoutGapFill_WhenLongFormSeasonListsBareNumbers()
    {
        // Arrange
        var title = "Breaking Bad Complete Season 1 2 3 4 5 Extras BluRay 1080p";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        Assert.That(parsed.Seasons, Is.EqualTo([1, 2, 3, 4, 5]));
    }

    [Test]
    public void Parse_ListsSparseSeasons_WhenLongFormSeasonOmitsMiddleValues()
    {
        // Arrange
        var title = "Show Complete Season 1 3 Extras";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        Assert.That(parsed.Seasons, Is.EqualTo([1, 3]));
    }

    [Test]
    public void Parse_RejectsEndEpisode_When480pFollowsNumXNumToken()
    {
        // Arrange
        var title = "show 2x03 480p";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Seasons, Is.EqualTo([2]));
            Assert.That(parsed.Episodes, Is.EqualTo([3]));
        }
    }

    [Test]
    public void Parse_PopulatesEpisode_WhenLongFormSeasonEpisodeIsPresent()
    {
        // Arrange
        var title = "show season 1 episode 5 hdtv";

        // Act
        var parsed = title.ParseTvReleaseTitle();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Seasons, Is.EqualTo([1]));
            Assert.That(parsed.Episodes, Is.EqualTo([5]));
            Assert.That(parsed.TitleTokens, Is.EqualTo(["show"]));
        }
    }
}
