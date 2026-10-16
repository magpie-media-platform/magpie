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

    [TestCase("yuv420p10le", 10)]
    [TestCase("p010le", 10)]
    [TestCase("yuv444p12be", 12)]
    [TestCase("yuv420p", 8)]
    [TestCase("yuvj420p", 8)]
    [TestCase("pal8", null)]
    [TestCase("rgb24", null)]
    [TestCase(null, null)]
    public void BitDepthFromPixelFormat_ReturnsTheBitsPerSample_WhenThePixelFormatNamesThem(
        string? pixelFormat,
        int? expected)
    {
        // Act
        var result = FFProbeMediaInfoExtractor.BitDepthFromPixelFormat(pixelFormat);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }
}
