using System.Text.Json;
using System.Text.Json.Nodes;

using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public sealed class CollectionFilterOperatorTests
{
    [TestCase("PrimaryMediaSize null")]
    [TestCase("PrimaryMediaFileCount !null")]
    [TestCase("PrimaryMediaSize >= 4294967296 and PrimaryMediaFileCount = 2")]
    public void FormatContentFilter_RoundTripsMediaTotals_WhenStoredFieldsAreUsed(string query)
    {
        // Arrange
        var parsed = query.ParseContentFilter();

        // Act
        var formatted = parsed.Value!.FormatContentFilter();
        var reparsed = formatted.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(reparsed.IsSuccess, Is.True);
            Assert.That(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(parsed.Value),
                JsonSerializer.SerializeToNode(reparsed.Value)), Is.True);
        }
    }

    [TestCase("Genres has (Name null)")]
    [TestCase("Tags has (Name !null)")]
    [TestCase(
        "Awards has (Body null and Name !null and Category null and Status !null and Year null and Language !null and Country null)")]
    [TestCase("ExternalIds has (Source null and Value !null)")]
    [TestCase("ContentRatings has (Body null and Rating !null)")]
    [TestCase(
        "Images has (Type null and MimeType !null and Url null and Source !null and Language null and Width !null and Height null)")]
    [TestCase(
        "Relationships has (Kind null and Type !null and Qualifier null and Subject !null and RelatedMetadataContainerId null and ExternalId !null and ExternalIdSource null)")]
    [TestCase("MediaFiles has (FileKind null and MimeType !null and FilePath null and FileSize !null)")]
    public void FormatContentFilter_RoundTripsCriteria_WhenCollectionFieldsUseNullChecks(string query)
    {
        // Arrange
        var parsed = query.ParseContentFilter();

        // Act
        var formatted = parsed.Value!.FormatContentFilter();
        var reparsed = formatted.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(reparsed.IsSuccess, Is.True);
            Assert.That(
                JsonNode.DeepEquals(
                    JsonSerializer.SerializeToNode(parsed.Value),
                    JsonSerializer.SerializeToNode(reparsed.Value)),
                Is.True);
        }
    }

    [TestCase("Tags has (Name null)")]
    [TestCase("Awards has (Category !null and Year null)")]
    [TestCase("ExternalIds has (Source null and Value !null)")]
    [TestCase("Images has (Language null and Width !null)")]
    public void FormatSubjectFilter_RoundTripsCriteria_WhenCollectionFieldsUseNullChecks(string query)
    {
        // Arrange
        var parsed = query.ParseSubjectFilter();

        // Act
        var formatted = parsed.Value!.FormatSubjectFilter();
        var reparsed = formatted.ParseSubjectFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(reparsed.IsSuccess, Is.True);
            Assert.That(
                JsonNode.DeepEquals(
                    JsonSerializer.SerializeToNode(parsed.Value),
                    JsonSerializer.SerializeToNode(reparsed.Value)),
                Is.True);
        }
    }

    [TestCase("Genres has (Name > \"x\")", "GreaterThan", "Name")]
    [TestCase("Tags has (Name > \"x\")", "GreaterThan", "Name")]
    [TestCase("Awards has (Body > \"x\")", "GreaterThan", "Body")]
    [TestCase("Awards has (Year contains 2020)", "Contains", "Year")]
    [TestCase("ExternalIds has (Source > \"x\")", "GreaterThan", "Source")]
    [TestCase("ContentRatings has (Rating > \"x\")", "GreaterThan", "Rating")]
    [TestCase("Images has (Type > \"x\")", "GreaterThan", "Type")]
    [TestCase("Images has (Url contains \"https://images.test/x\")", "Contains", "Url")]
    [TestCase("Images has (Width contains 100)", "Contains", "Width")]
    [TestCase("Awards has (Year in (2020))", "In", "Year")]
    [TestCase(
        "Image has (Url !in (\"https://images.test/x\")) and Images has (Type = \"poster\")",
        "NotIn",
        "Url")]
    [TestCase("Relationships has (Kind > \"x\")", "GreaterThan", "Kind")]
    [TestCase("Relationships has (Subject !startswith \"x\")", "NotStartsWith", "Subject")]
    [TestCase(
        "Relationships has (RelatedMetadataContainerId contains \"3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c\")",
        "Contains",
        "RelatedMetadataContainerId")]
    [TestCase("Relationships has (ExternalId > \"x\")", "GreaterThan", "ExternalId")]
    public void ParseContentFilter_ReturnsFieldAndOperator_WhenCollectionOperatorIsUnsupported(
        string query,
        string expectedOperator,
        string expectedField)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Value.Message, Does.Contain(expectedOperator));
            Assert.That(result.Error!.Value.Message, Does.Contain(expectedField));
        }
    }

    [TestCase("Tags has (Name > \"x\")", "GreaterThan", "Name")]
    [TestCase("Awards has (Body > \"x\")", "GreaterThan", "Body")]
    [TestCase("ExternalIds has (Value > \"x\")", "GreaterThan", "Value")]
    [TestCase("Images has (Height contains 100)", "Contains", "Height")]
    [TestCase("Award has (Year in (2020))", "In", "Year")]
    [TestCase(
        "Images has (Url !in (\"https://images.test/x\")) and Kind = \"person\"",
        "NotIn",
        "Url")]
    public void ParseSubjectFilter_ReturnsFieldAndOperator_WhenCollectionOperatorIsUnsupported(
        string query,
        string expectedOperator,
        string expectedField)
    {
        // Arrange

        // Act
        var result = query.ParseSubjectFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Value.Message, Does.Contain(expectedOperator));
            Assert.That(result.Error!.Value.Message, Does.Contain(expectedField));
        }
    }
}
