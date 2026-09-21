using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Magpie.Extensions.Qbittorrent;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class TorrentBytesExtensionsTests
{
    [Test]
    public void ComputeInfoHash_MatchesSha1OfInfoDict_WhenSingleFileTorrent()
    {
        // Arrange
        var pieces = Enumerable.Range(0, 20).Select(index => (byte)index).ToArray();
        var infoDict = Encoding.ASCII
            .GetBytes("d6:lengthi100e4:name8:test.txt12:piece lengthi65536e6:pieces20:")
            .Concat(pieces)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();
        const string announce = "http://tracker.example/announce";
        var torrent = Encoding.ASCII.GetBytes($"d8:announce{announce.Length}:{announce}4:info")
            .Concat(infoDict)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();

        // Act
        var hash = torrent.ComputeInfoHash();

        // Assert
        using var sha = SHA1.Create();
        var expected = Convert.ToHexString(sha.ComputeHash(infoDict)).ToLowerInvariant();
        Assert.That(hash, Is.EqualTo(expected));
        Assert.That(hash, Has.Length.EqualTo(40));
    }

    [Test]
    public void ComputeInfoHash_MatchesSha1OfInfoDict_WhenMultiFileTorrent()
    {
        // Arrange
        var pieces = Enumerable.Range(0, 60).Select(index => (byte)index).ToArray();
        var infoDict = Encoding.ASCII.GetBytes(
            "d5:filesld6:lengthi12345e4:pathl5:a.mkveed6:lengthi200e4:pathl3:sub5:b.srteee" +
            "4:name14:release-folder12:piece lengthi131072e6:pieces60:")
            .Concat(pieces)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();
        const string announce = "udp://tracker.example:80";
        var torrent = Encoding.ASCII.GetBytes($"d8:announce{announce.Length}:{announce}4:info")
            .Concat(infoDict)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();

        // Act
        var hash = torrent.ComputeInfoHash();

        // Assert
        using var sha = SHA1.Create();
        var expected = Convert.ToHexString(sha.ComputeHash(infoDict)).ToLowerInvariant();
        Assert.That(hash, Is.EqualTo(expected));
    }

    [Test]
    public void ComputeInfoHash_StillFindsInfo_WhenTorrentWithNestedDictsBeforeInfo()
    {
        // Arrange
        const string primary = "http://primary.example/announce";
        const string backup = "http://backup.example/announce";
        var pieces = Enumerable.Range(0, 20).Select(index => (byte)index).ToArray();
        var infoDict = Encoding.ASCII
            .GetBytes("d6:lengthi42e4:name10:nested.bin12:piece lengthi65536e6:pieces20:")
            .Concat(pieces)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();
        var torrent = Encoding.ASCII.GetBytes(
            $"d8:announce{primary.Length}:{primary}13:announce-listll{primary.Length}:{primary}" +
            $"{backup.Length}:{backup}ee4:info")
            .Concat(infoDict)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();

        // Act
        var hash = torrent.ComputeInfoHash();

        // Assert
        using var sha = SHA1.Create();
        var expected = Convert.ToHexString(sha.ComputeHash(infoDict)).ToLowerInvariant();
        Assert.That(hash, Is.EqualTo(expected));
    }

    [Test]
    public void ComputeInfoHash_Throws_WhenMissingInfoKey()
    {
        // Arrange
        var torrent = Encoding.ASCII.GetBytes("d8:announce23:http://example/announcee");

        // Act
        var action = new TestDelegate(() => torrent.ComputeInfoHash());

        // Assert
        Assert.Throws<InvalidDataException>(action);
    }

    [Test]
    public void ComputeInfoHash_Throws_WhenNotADictionary()
    {
        // Arrange
        var torrent = Encoding.ASCII.GetBytes("l5:helloe");

        // Act
        var action = new TestDelegate(() => torrent.ComputeInfoHash());

        // Assert
        Assert.Throws<InvalidDataException>(action);
    }

    [Test]
    public void ComputeInfoHash_Throws_WhenMalformedString()
    {
        // Arrange
        var torrent = Encoding.ASCII.GetBytes("d100:abce");

        // Act
        var action = new TestDelegate(() => torrent.ComputeInfoHash());

        // Assert
        Assert.Throws<InvalidDataException>(action);
    }

    [Test]
    public void ComputeInfoHash_Throws_WhenNestingBeyondCap()
    {
        // Arrange
        var torrent = Encoding.ASCII.GetBytes("d4:info" + new string('l', 500) + new string('e', 501));

        // Act
        var action = new TestDelegate(() => torrent.ComputeInfoHash());

        // Assert
        Assert.Throws<InvalidDataException>(action);
    }

    [Test]
    public void ComputeInfoHash_StillParses_WhenDeeplyButValidlyNestedWithinCap()
    {
        // Arrange
        var pieces = Enumerable.Range(0, 20).Select(index => (byte)index).ToArray();
        var infoDict = Encoding.ASCII
            .GetBytes("d6:lengthi7e4:name8:deep.bin12:piece lengthi65536e6:pieces20:")
            .Concat(pieces)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();
        var torrent = Encoding.ASCII
            .GetBytes("d4:deep" + new string('l', 50) + new string('e', 50) + "4:info")
            .Concat(infoDict)
            .Concat(Encoding.ASCII.GetBytes("e"))
            .ToArray();

        // Act
        var hash = torrent.ComputeInfoHash();

        // Assert
        using var sha = SHA1.Create();
        var expected = Convert.ToHexString(sha.ComputeHash(infoDict)).ToLowerInvariant();
        Assert.That(hash, Is.EqualTo(expected));
    }
}
