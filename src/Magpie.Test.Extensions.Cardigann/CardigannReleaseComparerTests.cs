using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Cardigann;
using Magpie.Models.Data;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann;

[TestFixture]
public sealed class CardigannReleaseComparerTests
{
    [TestCase("""{"seeders":10,"leechers":1}""", """{"seeders":9,"leechers":999}""", -1)]
    [TestCase("""{"seeders":"10","leechers":"1"}""", """{"seeders":"10","leechers":"2"}""", 1)]
    [TestCase("""not-json""", """{"seeders":1,"leechers":0}""", 0)]
    [TestCase("""{"seeders":"many","leechers":10}""", """{"seeders":1,"leechers":9}""", 0)]
    [TestCase("""{"seeders":"many"}""", """{"seeders":1,"leechers":9}""", 0)]
    [TestCase("""[]""", """not-json""", 0)]
    public void Compare_ReturnsExpectedOrdering_WhenPeerMetadataVaries(
        string leftProperties,
        string rightProperties,
        int expected)
    {
        // Arrange
        var comparer = new CardigannReleaseComparer();
        var left = new Release
        {
            Protocol = "torrent",
            Title = "Left",
            Properties = leftProperties,
        };
        var right = new Release
        {
            Protocol = "torrent",
            Title = "Right",
            Properties = rightProperties,
        };

        // Act
        var result = comparer.Compare(left, right);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("""{"seeders":10,"leechers":1}""", true)]
    [TestCase("""{"seeders":"10","leechers":"1"}""", true)]
    [TestCase("""{"seeders":10}""", false)]
    [TestCase("""{"leechers":1}""", false)]
    [TestCase("""{"seeders":"many","leechers":1}""", false)]
    [TestCase("""{"seeders":10,"leechers":"many"}""", false)]
    [TestCase("""{"seeders":-1,"leechers":1}""", false)]
    [TestCase("""{"seeders":1,"leechers":"-1"}""", false)]
    [TestCase("""not-json""", false)]
    public void CanCompare_ReturnsExpectedFlag_WhenPeerMetadataVaries(string properties, bool expected)
    {
        // Arrange
        var comparer = new CardigannReleaseComparer();
        var release = new Release
        {
            Protocol = "torrent",
            Title = "Release",
            Properties = properties,
        };

        // Act
        var result = comparer.CanCompare(release);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Compare_OrdersReleasesByPeerMetadata_WhenThreeReleasesHaveCompletePeerMetadata()
    {
        // Arrange
        var comparer = new CardigannReleaseComparer();
        var releases = new List<Release>
        {
            new()
            {
                Protocol = "torrent",
                Title = "B",
                Properties = """{"seeders":2,"leechers":3}""",
            },
            new()
            {
                Protocol = "torrent",
                Title = "C",
                Properties = """{"seeders":1,"leechers":2}""",
            },
            new()
            {
                Protocol = "torrent",
                Title = "A",
                Properties = """{"seeders":3,"leechers":1}""",
            },
        };

        // Act
        releases.Sort(comparer.Compare);

        // Assert
        Assert.That(releases.Select(release => release.Title), Is.EqualTo(["A", "B", "C"]));
    }
}
