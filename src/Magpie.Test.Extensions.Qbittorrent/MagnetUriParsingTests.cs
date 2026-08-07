using System;

using Magpie.Extensions.Qbittorrent;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class MagnetUriParsingTests
{
    [Test]
    public void ExtractV1InfoHash_LowercaseHash_WhenV1HashIsUppercaseHex()
    {
        // Arrange
        const string magnet = "magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01" +
            "&dn=Ubuntu+22.04&tr=udp%3A%2F%2Ftracker.example%3A80%2Fannounce";

        // Act
        var hash = QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet);

        // Assert
        Assert.That(hash, Is.EqualTo("abcdef0123456789abcdef0123456789abcdef01"));
    }

    [Test]
    public void ExtractV1InfoHash_UnchangedHash_WhenV1HashIsLowercaseHex()
    {
        // Arrange
        const string magnet = "magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01";

        // Act
        var hash = QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet);

        // Assert
        Assert.That(hash, Is.EqualTo("abcdef0123456789abcdef0123456789abcdef01"));
    }

    [Test]
    public void ExtractV1InfoHash_LowercaseHex_WhenV1HashIsBase32()
    {
        // Arrange
        const string magnet = "magnet:?xt=urn:btih:CWZWWBKKVTPUYEKCKBTPMD3AAAAAAAAA";

        // Act
        var hash = QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet);

        // Assert
        Assert.That(hash, Is.EqualTo("15b36b054aacdf4c11425066f60f600000000000"));
    }

    [Test]
    public void ExtractV1InfoHash_EquivalentHex_WhenV1HashIsAllZeroBase32()
    {
        // Arrange
        const string base32Magnet = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        // Act
        var hash = QbittorrentDownloadClientIntegration.ExtractV1InfoHash(base32Magnet);

        // Assert
        Assert.That(hash, Is.EqualTo(new string('0', 40)));
    }

    [Test]
    public void ExtractV1InfoHash_NotSupportedException_WhenMagnetContainsV2Multihash()
    {
        // Arrange
        const string magnet = "magnet:?xt=urn:btmh:1220" +
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

        // Act
        var ex = Assert.Throws<NotSupportedException>(
            () => QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet));

        // Assert
        Assert.That(ex!.Message, Does.Contain("v1"));
    }

    [Test]
    public void ExtractV1InfoHash_ArgumentException_WhenMagnetHasNoXtParameter()
    {
        // Arrange
        const string magnet = "magnet:?dn=NoHashHere&tr=udp%3A%2F%2Ftracker.example";

        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet));

        // Assert
        Assert.That(ex!.Message, Does.Contain("xt"));
    }

    [Test]
    public void ExtractV1InfoHash_ArgumentException_WhenSchemeIsNotMagnet()
    {
        // Arrange
        const string url = "https://example.com/file.torrent";

        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => QbittorrentDownloadClientIntegration.ExtractV1InfoHash(url));

        // Assert
        Assert.That(ex!.Message, Does.Contain("magnet:"));
    }

    [Test]
    public void ExtractV1InfoHash_ArgumentException_WhenInputIsEmpty()
    {
        // Arrange
        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => QbittorrentDownloadClientIntegration.ExtractV1InfoHash(string.Empty));

        // Assert
        Assert.That(ex!.Message, Does.Contain("empty"));
    }

    [Test]
    public void ExtractV1InfoHash_ArgumentException_WhenHashLengthIsInvalid()
    {
        // Arrange
        const string magnet = "magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef0";

        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet));

        // Assert
        Assert.That(ex!.Message, Does.Contain("base32").Or.Contain("hex"));
    }

    [Test]
    public void ExtractV1InfoHash_V1Hash_WhenV1PrecedesV2()
    {
        // Arrange
        const string magnet =
            "magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01" +
            "&xt=urn:btmh:1220abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

        // Act
        var hash = QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet);

        // Assert
        Assert.That(hash, Is.EqualTo("abcdef0123456789abcdef0123456789abcdef01"));
    }

    [Test]
    public void ExtractV1InfoHash_ArgumentException_WhenXtValueIsUrlEncoded()
    {
        // Arrange
        const string magnet = "magnet:?xt=urn%3Abtih%3Aabcdef0123456789abcdef0123456789abcdef01";

        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet));

        // Assert
        Assert.That(ex!.Message, Does.Contain("xt"));
    }

    [Test]
    public void ExtractV1InfoHash_ArgumentException_WhenBase32ValueIsInvalid()
    {
        // Arrange
        const string magnet = "magnet:?xt=urn:btih:0123456789abcdefghijklmnopqrstuv";

        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => QbittorrentDownloadClientIntegration.ExtractV1InfoHash(magnet));

        // Assert
        Assert.That(ex!.Message, Does.Contain("base32"));
    }
}
