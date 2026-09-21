using System;
using System.Linq;

using Magpie.Models.Data;
using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class FilterQueryExtensionsTests
{
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
