using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using AngleSharp.Html.Parser;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann.Engine;

[TestFixture]
public sealed class ResultParserTests
{
    private const string Btih = "abcdef0123456789abcdef0123456789abcdef01";

    private const string DetailHtml =
        """<html><body><a class="magnet" href="magnet:?xt=urn:btih:""" +
        Btih +
        """&amp;dn=cool">grab</a></body></html>""";

    private const string Html = """
        <table><tr class="row">
            <td class="title">Cool Movie 2020 1080p</td>
            <td class="details"><a href="/details/42">link</a></td>
            <td class="download"><a href="/download/passkey">download</a></td>
            <td class="comments"><a href="/comments/42">comments</a></td>
            <td class="guid">https://tracker.test/guid/42</td>
            <td class="link">https://tracker.test/link/42</td>
            <td class="magnet-url">magnet:?xt=urn:btih:operational</td>
            <td class="download-url">https://tracker.test/download-url/42</td>
            <td class="details-url">details/42</td>
            <td class="comments-url">https://tracker.test/comments-url/42</td>
            <td class="guid-url">https://tracker.test/guid-url/42</td>
            <td class="link-url">https://tracker.test/link-url/42</td>
            <td class="seeders">100</td>
            <td class="leechers">5</td>
            <td class="infohash">abcdef0123456789abcdef0123456789abcdef01</td>
            <td class="date">2020-01-02</td>
            <td class="resolution">1080p</td>
            <td class="source">bluray</td>
            <td class="poster">https://tracker.test/poster/42.jpg</td>
        </tr></table>
        """;

    private IResultParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new ResultParser(TimeProvider.System, NullLogger<ResultParser>.Instance);
    }

    [Test]
    public void ExtractDownloadTarget_ReturnsAttributeValue_WhenSelectorMatches()
    {
        // Arrange
        var download = new DownloadBlock { Selector = "a.magnet", Attribute = "href" };

        // Act
        var target = _parser.ExtractDownloadTarget(DetailHtml, download);

        // Assert
        Assert.That(target, Is.EqualTo("magnet:?xt=urn:btih:" + Btih + "&dn=cool"));
    }

    [Test]
    public void ExtractDownloadTarget_ReturnsNull_WhenSelectorMisses()
    {
        // Arrange
        var download = new DownloadBlock { Selector = "a.torrent", Attribute = "href" };

        // Act
        var target = _parser.ExtractDownloadTarget(DetailHtml, download);

        // Assert
        Assert.That(target, Is.Null);
    }

    [Test]
    public void ExtractDownloadTarget_BuildsMagnet_WhenInfoHashBlockMatches()
    {
        // Arrange
        const string html = """
            <html><body>
                <span class="hash">abcdef0123456789abcdef0123456789abcdef01</span>
                <span class="name">Cool Movie 2020 1080p</span>
            </body></html>
            """;
        var download = new DownloadBlock
        {
            InfoHash = new InfoHashBlock
            {
                Hash = new FieldDefinition { Selector = "span.hash" },
                Title = new FieldDefinition { Selector = "span.name" },
            },
        };

        // Act
        var target = _parser.ExtractDownloadTarget(html, download);

        // Assert
        Assert.That(
            target,
            Does.StartWith(
                "magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01&dn=Cool%20Movie%202020%201080p"));
    }

    [Test]
    public void ApplyResolvedDownload_CapturesInfoHashAndExternalId_WhenMagnetUri()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "Cool Movie 2020 1080p" };
        var resolved = new Uri("magnet:?xt=urn:btih:" + Btih + "&dn=cool");

        // Act
        _parser.ApplyResolvedDownload(release, resolved);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(release.InfoUrl, Is.EqualTo(resolved));
            Assert.That(release.ExternalId, Is.EqualTo(Btih));
            Assert.That(release.IdentityKeys, Has.Count.EqualTo(1));
            Assert.That(release.IdentityKeys.Single().Authority, Is.EqualTo("btih"));
            Assert.That(release.IdentityKeys.Single().Value, Is.EqualTo(Btih));
        }
    }

    [Test]
    public void ApplyResolvedDownload_UsesSameExternalId_WhenMagnetQueryChanges()
    {
        // Arrange
        var first = new Release { Protocol = "torrent", Title = "Cool Movie 2020 1080p" };
        var second = new Release { Protocol = "torrent", Title = "Cool Movie 2020 1080p" };

        // Act
        _parser.ApplyResolvedDownload(
            first,
            new Uri("magnet:?xt=urn:btih:" + Btih + "&tr=https%3A%2F%2Ftracker.test%2Fone&token=first"));
        _parser.ApplyResolvedDownload(
            second,
            new Uri("magnet:?xt=urn:btih:" + Btih + "&tr=https%3A%2F%2Ftracker.test%2Ftwo&token=second"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExternalId, Is.EqualTo(Btih));
            Assert.That(second.ExternalId, Is.EqualTo(Btih));
        }
    }

    [Test]
    public void Parse_CapturesInfoHash_WhenHtmlRowContainsMagnet()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "test" };
        definition.Search.Rows.Selector = "tr.release";
        definition.Search.Fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal)
        {
            ["title"] = new() { Selector = "td.title" },
            ["magnet"] = new() { Selector = "a.magnet", Attribute = "href" },
        };

        // Act
        var releases = _parser.Parse(
            """
            <table><tr class="release">
                <td class="title">Cool Movie</td>
                <td><a class="magnet" href="magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01">grab</a></td>
            </tr></table>
            """,
            definition,
            new Uri("https://tracker.test/"),
            new Dictionary<string, object?>());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(releases[0].ExternalId, Is.EqualTo(Btih));
            Assert.That(releases[0].IdentityKeys, Has.Count.EqualTo(1));
            Assert.That(releases[0].IdentityKeys.Single().Authority, Is.EqualTo("btih"));
            Assert.That(releases[0].IdentityKeys.Single().Value, Is.EqualTo(Btih));
        }
    }

    [Test]
    public void ParseJson_LeavesIdentityEmpty_WhenInfoHashIsInvalid()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "test" };
        definition.Search.Rows.Selector = "$";
        definition.Search.Fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal)
        {
            ["title"] = new() { Selector = "title" },
            ["infohash"] = new() { Selector = "infohash" },
        };

        // Act
        var releases = _parser.ParseJson(
            """[{"title":"Cool Movie","infohash":"not-a-valid-hash"}]""",
            definition,
            new Uri("https://tracker.test/"),
            new Dictionary<string, object?>());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(releases[0].ExternalId, Is.EqualTo("not-a-valid-hash"));
            Assert.That(releases[0].IdentityKeys, Is.Empty);
        }
    }

    [Test]
    public void ApplyResolvedDownload_LeavesIdentityKeysEmpty_WhenResolvedUriIsNotAMagnet()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "Cool Movie 2020 1080p" };
        var resolved = new Uri("https://tracker.test/download/42.torrent");

        // Act
        _parser.ApplyResolvedDownload(release, resolved);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(release.IdentityKeys, Is.Empty);
            Assert.That(release.ExternalId, Is.EqualTo("https://tracker.test/download/42.torrent"));
        }
    }

    [Test]
    public void Parse_MapsCategoryConsistently_WhenMultipleRowsShareACategory()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "test" };
        definition.Search.Rows.Selector = "tr.row";
        definition.Search.Fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal)
        {
            ["title"] = new() { Selector = "td.title" },
            ["details"] = new() { Selector = "td.details a", Attribute = "href" },
            ["category"] = new() { Selector = "td.cat" },
        };
        definition.Caps.CategoryMappings.Add(new CategoryMapping { Id = "7", Cat = "Movies/HD" });

        const string html = """
            <table>
                <tr class="row">
                    <td class="title">A</td>
                    <td class="details"><a href="/a">a</a></td>
                    <td class="cat">7</td>
                </tr>
                <tr class="row">
                    <td class="title">B</td>
                    <td class="details"><a href="/b">b</a></td>
                    <td class="cat">7</td>
                </tr>
                <tr class="row">
                    <td class="title">C</td>
                    <td class="details"><a href="/c">c</a></td>
                    <td class="cat">99</td>
                </tr>
            </table>
            """;

        // Act
        var releases = _parser.Parse(html, definition, new Uri("https://tracker.test/"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releases, Has.Count.EqualTo(3));
            Assert.That(releases[0].Categories, Is.EqualTo(["Movies/HD"]));
            Assert.That(releases[1].Categories, Is.EqualTo(["Movies/HD"]));
            Assert.That(releases[2].Categories, Is.EqualTo(["99"]));
        }
    }

    [Test]
    public void ExtractField_ReturnsFilteredValue_WhenSelectorMatches()
    {
        // Arrange
        using var document = new HtmlParser().ParseDocument(
            """<html><body><span class="value"> mixed Case </span></body></html>""");
        var field = new FieldDefinition
        {
            Selector = "span.value",
            Filters = [new FieldFilter { Name = "toupper" }],
        };

        // Act
        var result = _parser.ExtractField(document.DocumentElement, field, "test.field");

        // Assert
        Assert.That(result, Is.EqualTo("MIXED CASE"));
    }

    [Test]
    public void Parse_RetainsDisplayFields_WhenOperationalUrlFieldsArePresent()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "test" };
        definition.Search.Rows.Selector = "tr.row";
        definition.Search.Fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal)
        {
            ["title"] = new() { Selector = "td.title" },
            ["details"] = new() { Selector = "td.details a", Attribute = "href" },
            ["download"] = new() { Selector = "td.download a", Attribute = "href" },
            ["comments"] = new() { Selector = "td.comments a", Attribute = "href" },
            ["guid"] = new() { Selector = "td.guid" },
            ["link"] = new() { Selector = "td.link" },
            ["magnet_url"] = new() { Selector = "td.magnet-url" },
            ["download_url"] = new() { Selector = "td.download-url" },
            ["details_url"] = new() { Selector = "td.details-url" },
            ["comments_url"] = new() { Selector = "td.comments-url" },
            ["guid_url"] = new() { Selector = "td.guid-url" },
            ["link_url"] = new() { Selector = "td.link-url" },
            ["seeders"] = new() { Selector = "td.seeders" },
            ["leechers"] = new() { Selector = "td.leechers" },
            ["infohash"] = new() { Selector = "td.infohash" },
            ["date"] = new() { Selector = "td.date" },
            ["resolution"] = new() { Selector = "td.resolution" },
            ["source"] = new() { Selector = "td.source" },
            ["poster"] = new() { Selector = "td.poster" },
        };

        // Act
        var releases = _parser.Parse(Html, definition, new Uri("https://tracker.test/"));

        // Assert
        Assert.That(releases, Has.Count.EqualTo(1));
        var properties = JsonSerializer.Deserialize<Dictionary<string, string?>>(releases[0].Properties!)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(properties["seeders"], Is.EqualTo("100"));
            Assert.That(properties["leechers"], Is.EqualTo("5"));
            Assert.That(properties["infohash"], Is.EqualTo("abcdef0123456789abcdef0123456789abcdef01"));
            Assert.That(properties["date"], Is.EqualTo("2020-01-02"));
            Assert.That(properties["resolution"], Is.EqualTo("1080p"));
            Assert.That(properties["source"], Is.EqualTo("bluray"));
            Assert.That(properties["poster"], Is.EqualTo("https://tracker.test/poster/42.jpg"));
            Assert.That(properties.ContainsKey("title"), Is.True);
            Assert.That(properties.ContainsKey("download"), Is.False);
            Assert.That(properties.ContainsKey("details"), Is.False);
            Assert.That(properties.ContainsKey("comments"), Is.False);
            Assert.That(properties.ContainsKey("guid"), Is.False);
            Assert.That(properties.ContainsKey("link"), Is.False);
            Assert.That(properties.ContainsKey("magnet_url"), Is.False);
            Assert.That(properties.ContainsKey("download_url"), Is.False);
            Assert.That(properties.ContainsKey("details_url"), Is.False);
            Assert.That(properties.ContainsKey("comments_url"), Is.False);
            Assert.That(properties.ContainsKey("guid_url"), Is.False);
            Assert.That(properties.ContainsKey("link_url"), Is.False);
        }
    }

    [Test]
    public void Parse_MaterializesPropertyUrlsAsAbsolute_WhenTheSelectedValuesAreRelative()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "test" };
        definition.Search.Rows.Selector = "tr.row";
        definition.Search.Fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal)
        {
            ["title"] = new() { Selector = "td.title" },
            ["details"] = new() { Selector = "td.details a", Attribute = "href" },
            ["poster"] = new() { Selector = "td.poster" },
            ["banner"] = new() { Selector = "td.banner" },
            ["screenshot_url"] = new() { Selector = "td.screenshot" },
            ["absolute_poster"] = new() { Selector = "td.absolute" },
            ["seeders"] = new() { Selector = "td.seeders" },
            ["resolution"] = new() { Selector = "td.resolution" },
        };

        var html = """
            <table><tr class="row">
                <td class="title">Cool Movie 2020 1080p</td>
                <td class="details"><a href="/details/42">link</a></td>
                <td class="poster">/images/poster/42.jpg</td>
                <td class="banner">images/banner/42.jpg</td>
                <td class="screenshot">//static.tracker.test/shot/42.png</td>
                <td class="absolute">https://cdn.other.test/poster.jpg</td>
                <td class="seeders">100</td>
                <td class="resolution">1080p</td>
            </tr></table>
            """;

        // Act
        var releases = _parser.Parse(html, definition, new Uri("https://tracker.test/browse/"));

        // Assert
        Assert.That(releases, Has.Count.EqualTo(1));
        var properties = JsonSerializer.Deserialize<Dictionary<string, string?>>(releases[0].Properties!)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(properties["poster"], Is.EqualTo("https://tracker.test/images/poster/42.jpg"));
            Assert.That(properties["banner"], Is.EqualTo("https://tracker.test/browse/images/banner/42.jpg"));
            Assert.That(properties["screenshot_url"], Is.EqualTo("https://static.tracker.test/shot/42.png"));
            Assert.That(properties["absolute_poster"], Is.EqualTo("https://cdn.other.test/poster.jpg"));
            Assert.That(properties["seeders"], Is.EqualTo("100"));
            Assert.That(properties["resolution"], Is.EqualTo("1080p"));
        }
    }
}
