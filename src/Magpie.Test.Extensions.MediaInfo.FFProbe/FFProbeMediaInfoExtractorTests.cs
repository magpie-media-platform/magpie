using Magpie.Extensions.MediaInfo.FFProbe;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MediaInfo.FFProbe;

[TestFixture]
public sealed class FFProbeMediaInfoExtractorTests
{
    [TestCase(@"C:\Library\Books\Dune.epub: Invalid data found when processing input", true)]
    [TestCase(@"C:\Library\Books\Dune.pdf: Invalid data found when processing input", true)]
    [TestCase(@"C:\Library\Books\Dune.epub: Permission denied", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsUnreadableFormat_IsTrueOnlyForInvalidInput_WhenFfprobeFails(string? errorOutput, bool expected)
    {
        // Act
        var result = FFProbeMediaInfoExtractor.IsUnreadableFormat(errorOutput);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }
}
