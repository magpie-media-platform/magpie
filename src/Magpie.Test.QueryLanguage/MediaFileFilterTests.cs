using System;
using System.Linq;
using System.Text.Json;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public sealed class MediaFileFilterTests
{
    [Test]
    public void ParseContentFilter_PutsConditionsOnOneFile_WhenOneHasExpressionJoinsThemWithAnd()
    {
        // Arrange
        const string query =
            "mediaFiles has (fileKind = Video and mimeType = \"video/mp4\" and filePath contains \"/m/\" and " +
            "fileSize >= 1000)";

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var mediaFiles = result.Value!.ContentHierarchyItem.And.Single().MediaFiles;
        var file = mediaFiles.Some.Single().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.FileKind.And.Single().Value, Is.EqualTo("Video"));
            Assert.That(file.MimeType.And.Single().Value, Is.EqualTo("video/mp4"));
            Assert.That(file.FilePath.And.Single().Operator, Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(file.FilePath.And.Single().Value, Is.EqualTo("/m/"));
            Assert.That(file.FileSize.And.Single().Operator, Is.EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(file.FileSize.And.Single().Value, Is.EqualTo(1000L));
            Assert.That(mediaFiles.None, Is.Empty);
        }
    }

    [Test]
    public void ParseContentFilter_KeepsFilesIndependent_WhenSeparateHasExpressionsAreJoinedByAnd()
    {
        // Arrange
        const string query = "mediaFiles has (fileKind = Video) and mediaFiles has (fileSize > 1000)";

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var some = result.Value!.ContentHierarchyItem.And.Single().MediaFiles.Some;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(some, Has.Count.EqualTo(2));
            Assert.That(some[0].Single().FileKind.And.Single().Value, Is.EqualTo("Video"));
            Assert.That(some[0].Single().FileSize.And, Is.Empty);
            Assert.That(some[1].Single().FileSize.And.Single().Value, Is.EqualTo(1000L));
            Assert.That(some[1].Single().FileKind.And, Is.Empty);
        }
    }

    [Test]
    public void ParseContentFilter_AddsAlternatives_WhenHasExpressionsAreJoinedByOr()
    {
        // Arrange
        const string query = "mediaFile has \"Video\" or mediaFile has \"Audio\"";

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var group = result.Value!.ContentHierarchyItem.And.Single().MediaFiles.Some.Single();
        Assert.That(group.Select(file => file.FileKind.And.Single().Value), Is.EqualTo(["Video", "Audio"]));
    }

    [Test]
    public void ParseContentFilter_AddsNoneCriteria_WhenNotHasIsUsed()
    {
        // Arrange
        const string query = "mediaFiles !has (fileKind = Subtitle)";

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var mediaFiles = result.Value!.ContentHierarchyItem.And.Single().MediaFiles;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mediaFiles.Some, Is.Empty);
            Assert.That(mediaFiles.None.Single().FileKind.And.Single().Value, Is.EqualTo("Subtitle"));
        }
    }

    [TestCase("mediaFiles has (fileSize = 5000000000)", CriteriaValueOperator.Equals)]
    [TestCase("mediaFiles has (fileSize != 5000000000)", CriteriaValueOperator.NotEquals)]
    [TestCase("mediaFiles has (fileSize > 5000000000)", CriteriaValueOperator.GreaterThan)]
    [TestCase("mediaFiles has (fileSize >= 5000000000)", CriteriaValueOperator.GreaterThanOrEqual)]
    [TestCase("mediaFiles has (fileSize < 5000000000)", CriteriaValueOperator.LessThan)]
    [TestCase("mediaFiles has (fileSize <= 5000000000)", CriteriaValueOperator.LessThanOrEqual)]
    public void ParseContentFilter_ParsesNumericSize_WhenFileSizeUsesComparison(
        string query,
        CriteriaValueOperator expected)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var criterion = result.Value!.ContentHierarchyItem.And.Single().MediaFiles.Some.Single().Single()
            .FileSize.And.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion.Operator, Is.EqualTo(expected));
            Assert.That(criterion.Value, Is.EqualTo(5000000000L));
        }
    }

    [TestCase("mediaFiles has (fileSize > \"big\")")]
    [TestCase("mediaFiles has (fileSize contains 1)")]
    [TestCase("mediaFiles has (duration > 1)")]
    public void ParseContentFilter_Fails_WhenMediaFileConditionIsInvalid(string query)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase("fileKind = Video", "fileKind is a media file field. Use mediaFiles has (fileKind ...)")]
    [TestCase("mimeType = \"video/mp4\"", "mimeType is a media file field. Use mediaFiles has (mimeType ...)")]
    [TestCase("filePath contains \"x\"", "filePath is a media file field. Use mediaFiles has (filePath ...)")]
    [TestCase("fileSize > 1", "fileSize is a media file field. Use mediaFiles has (fileSize ...)")]
    public void ParseContentFilter_FailsWithCollectionHint_WhenMediaFileFieldIsTopLevel(string query, string expected)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Value.Message, Is.EqualTo(expected));
        }
    }

    [TestCase(
        "MediaFiles has (FileKind = \"Video\" and FileSize > 1000)",
        "MediaFiles has (FileKind = \"Video\" and FileSize > 1000)")]
    [TestCase(
        "mediaFiles has (fileKind = Video) or mediaFiles has (fileKind = Audio)",
        "MediaFiles has (FileKind = \"Video\") or MediaFiles has (FileKind = \"Audio\")")]
    [TestCase(
        "monitoring = Monitored and mediaFiles !has (mimeType startswith \"text/\")",
        "Monitoring = Monitored and MediaFiles !has (MimeType startswith \"text/\")")]
    public void FormatContentFilter_WritesMediaFileCollection_WhenCriteriaHaveMediaFiles(string query, string expected)
    {
        // Arrange
        var criteria = query.ParseContentFilter().Value!;

        // Act
        var formatted = criteria.FormatContentFilter();

        // Assert
        Assert.That(formatted, Is.EqualTo(expected));
    }

    [Test]
    public void FormatContentFilter_ThrowsWithGuidance_WhenStoredCriteriaUseLegacyMediaFiles()
    {
        // Arrange
        var criteria = JsonSerializer.Deserialize<ContentFilterCriteria>(
            "{\"MediaFiles\":{\"And\":[{\"FileKind\":{\"And\":[{\"Value\":\"Video\"}]}}]}}")!;

        // Act
        var exception = Assert.Throws<ArgumentException>(() => criteria.FormatContentFilter());

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("The filter uses the removed top-level media file criteria."));
    }
}
