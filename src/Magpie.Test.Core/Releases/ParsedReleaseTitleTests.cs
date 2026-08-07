using Magpie.Core.Releases;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ParsedReleaseTitleTests
{
    [TestCase(null, 1, false, false, null)]
    [TestCase("", 1, false, false, null)]
    [TestCase("   ", 1, false, false, null)]
    [TestCase("Movie.2026.1080p-GRP", 1, false, false, "GRP")]
    [TestCase("Movie.2026.1080p-GRP   ", 1, false, false, "GRP")]
    [TestCase("Movie.2026.PROPER-GRP", 2, true, false, "GRP")]
    [TestCase("Movie.2026.PROPER2-GRP", 3, true, false, "GRP")]
    [TestCase("Movie.2026.REPACK-GRP", 2, false, true, "GRP")]
    [TestCase("Movie.2026.RERIP3-GRP", 4, false, true, "GRP")]
    [TestCase("Movie.2026.v5-GRP", 5, false, false, "GRP")]
    [TestCase("Movie.2026.PROPER2.v7-GRP", 7, true, false, "GRP")]
    [TestCase("Movie.2026.PROPER2.REPACK4.v3-GRP", 5, true, true, "GRP")]
    [TestCase("Movie.2026.v999999999999-GRP", 1, false, false, "GRP")]
    [TestCase("Movie.2026.vx-GRP", 1, false, false, "GRP")]
    [TestCase("Movie.2026.v2.v8-GRP", 2, false, false, "GRP")]
    [TestCase("Movie.2026.improper", 1, false, false, null)]
    public void Constructor_Values_WhenTitleIsParsed(
        string? title,
        int version,
        bool isProper,
        bool isRepack,
        string? group)
    {
        // Arrange
        var input = title;

        // Act
        var result = new ParsedReleaseTitle(input);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Revision.Version, Is.EqualTo(version));
            Assert.That(result.Revision.IsProper, Is.EqualTo(isProper));
            Assert.That(result.Revision.IsRepack, Is.EqualTo(isRepack));
            Assert.That(result.Group, Is.EqualTo(group));
        }
    }
}
