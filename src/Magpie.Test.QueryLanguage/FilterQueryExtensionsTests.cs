using System;
using System.Linq;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class FilterQueryExtensionsTests
{
    [TestCase("hasExternalIds = true", "Unknown field: hasExternalIds")]
    [TestCase("andField = 1", "Unknown field: andField")]
    [TestCase("orField = 1", "Unknown field: orField")]
    [TestCase("notField = 1", "Unknown field: notField")]
    [TestCase("inField = 1", "Unknown field: inField")]
    [TestCase("nullField = 1", "Unknown field: nullField")]
    [TestCase("containsField = 1", "Unknown field: containsField")]
    [TestCase("startswithField = 1", "Unknown field: startswithField")]
    [TestCase("endswithField=1", "Unknown field: endswithField")]
    [TestCase("genres has (hasName = \"x\")", "Unknown field hasName in collection Genres")]
    public void ParseContentFilter_ReadsWholeIdentifier_WhenFieldNameStartsWithKeyword(string query, string expected)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.Error?.Message, Is.EqualTo(expected));
    }

    [TestCase("hasSearchableTitle = true", true)]
    [TestCase("HasSearchableTitle=false", false)]
    [TestCase("monitoring = Monitored and (hasSearchableTitle != true)", false)]
    public void ParseContentFilter_SetsSearchableTitle_WhenFieldStartsWithHas(string query, bool expected)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.Value?.ContentHierarchyItem.And.Single().HasSearchableTitle, Is.EqualTo(expected));
    }

    [TestCase("monitoring = Shotgun", CriteriaValueOperator.Equals, "Monitoring = Shotgun")]
    [TestCase("Monitoring != Unmonitored", CriteriaValueOperator.NotEquals, "Monitoring != Unmonitored")]
    public void FormatContentFilter_RoundTripsMonitoring_WhenMonitoringIsFiltered(
        string query,
        CriteriaValueOperator expectedOperator,
        string expectedFormat)
    {
        // Arrange
        var parsed = query.ParseContentFilter();

        // Act
        var formatted = parsed.Value!.FormatContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                parsed.Value!.ContentHierarchyItem.And.Single().Monitoring.And.Single().Operator,
                Is.EqualTo(expectedOperator));
            Assert.That(formatted, Is.EqualTo(expectedFormat));
        }
    }

    [TestCase("importPending = true", true)]
    [TestCase("ImportPending=false", false)]
    [TestCase("monitoring = Monitored and (importPending != true)", false)]
    public void ParseContentFilter_SetsImportPending_WhenFieldIsImportPending(string query, bool expected)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.Value?.ContentHierarchyItem.And.Single().ImportPending, Is.EqualTo(expected));
    }

    [TestCase("genre has \"Action\"")]
    [TestCase("genre !has \"Action\"")]
    [TestCase("name in (\"a\", \"b\")")]
    [TestCase("name !in (\"a\")")]
    [TestCase("title contains \"x\" and monitoring = Monitored or title null")]
    [TestCase("genres has (name = \"a\") or genres has (name = \"b\")")]
    [TestCase("overview !null and title !contains \"x\"")]
    public void ParseFilterQuery_Succeeds_WhenKeywordsAreDelimited(string query)
    {
        // Arrange

        // Act
        var result = query.ParseFilterQuery();

        // Assert
        Assert.That(result.IsSuccess, Is.True);
    }

    [TestCase("RootFolderId in (null)")]
    [TestCase("Monitoring in (null)")]
    [TestCase("Name in (null)")]
    [TestCase("Duration in (null)")]
    [TestCase("Name in (\"a\", null)")]
    public void ParseContentFilter_Fails_WhenValueListContainsNull(string query)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase("PendingMetadataRefresh in (true)")]
    [TestCase("PendingMetadataRefresh null")]
    [TestCase("PendingMetadataRefresh !null")]
    [TestCase("PendingMetadataRefresh > true")]
    [TestCase("PendingMetadataRefresh contains true")]
    public void ParseContentFilter_Fails_WhenBooleanFieldUsesUnsupportedOperator(string query)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Value.Message, Does.Contain("is not supported for field"));
        }
    }

    [TestCase("PendingMetadataRefresh = true", true)]
    [TestCase("PendingMetadataRefresh != true", false)]
    public void ParseContentFilter_KeepsBooleanValue_WhenOperatorIsEqualsOrNotEquals(string query, bool expected)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Value!.ContentHierarchyItem.And.First().PendingMetadataRefresh,
                Is.EqualTo(expected));
        }
    }

    [TestCase("PendingMetadataRefresh = true and PendingMetadataRefresh = false")]
    [TestCase("PendingMetadataRefresh = true and PendingMetadataRefresh != true")]
    [TestCase("PendingMetadataRefresh != false and PendingMetadataRefresh = false")]
    public void ParseContentFilter_Fails_WhenBooleanFieldIsConstrainedTwiceWithDifferentValues(string query)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Error!.Value.Message,
                Does.Contain("is constrained more than once with conflicting values"));
        }
    }

    [Test]
    public void ParseContentFilter_KeepsTheConstraint_WhenBooleanFieldIsRepeatedWithTheSameValue()
    {
        // Arrange

        // Act
        var result = "PendingMetadataRefresh = true and PendingMetadataRefresh != false".ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.ContentHierarchyItem.And.First().PendingMetadataRefresh, Is.True);
        }
    }

    [TestCase("Duration > \"1h30m45s\"", 5445)]
    [TestCase("Duration > \"45s\"", 45)]
    [TestCase("Duration > \"1h30m\"", 5400)]
    [TestCase("Duration > \"90m\"", 5400)]
    [TestCase("Duration > \"2h\"", 7200)]
    public void ParseContentFilter_ReadsEveryDurationUnit_WhenValueIsShortForm(string query, int expectedSeconds)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Value!.ContentHierarchyItem.And.First().Metadata.And.First().Duration.And.First()
                    .Value.TotalSeconds,
                Is.EqualTo(expectedSeconds));
        }
    }

    [TestCase("Duration > \"99999999999999999999h\"")]
    [TestCase("Duration > \"999999999h999999999h999999999h\"")]
    public void ParseContentFilter_Fails_WhenShortDurationOverflows(string query)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void FormatContentFilter_ThrowsArgumentException_WhenCriteriaHoldMediaFiles()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            MediaFiles = new() { And = [new()] }
        };

        // Act
        var action = new TestDelegate(() => criteria.FormatContentFilter());

        // Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void FormatContentFilterOrDefault_ReturnsNull_WhenCriteriaHoldMediaFiles()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            MediaFiles = new() { And = [new()] }
        };

        // Act
        var result = criteria.FormatContentFilterOrDefault();

        // Assert
        Assert.That(result, Is.Null);
    }
}
