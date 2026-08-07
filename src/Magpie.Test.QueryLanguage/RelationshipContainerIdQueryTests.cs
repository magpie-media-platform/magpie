using System;
using System.Linq;

using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class RelationshipContainerIdQueryTests
{
    private const string Query =
        "Relationships has (RelatedMetadataContainerId = \"3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c\")";

    [Test]
    public void ParseContentFilter_BindsTheContainerId_WhenTheRelationshipNamesIt()
    {
        // Act
        var result = Query.ParseContentFilter();

        // Assert
        var criterion = result.Value!.ContentHierarchyItem.And
            .Single()
            .Metadata.And
            .Single()
            .Relationships.Some
            .Single()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                criterion.RelatedMetadataContainerId.And.Single().Value,
                Is.EqualTo(new Guid("3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c")));
            Assert.That(criterion.Subject.And, Is.Empty);
            Assert.That(criterion.ExternalIds.Some, Is.Empty);
        }
    }

    [Test]
    public void FormatFilterQuery_KeepsTheContainerId_WhenTheCriteriaRoundTrip()
    {
        // Arrange
        var criteria = Query.ParseContentFilter().Value!;

        // Act
        var result = criteria.FormatFilterQuery();

        // Assert
        Assert.That(result, Is.EqualTo(Query));
    }
}
