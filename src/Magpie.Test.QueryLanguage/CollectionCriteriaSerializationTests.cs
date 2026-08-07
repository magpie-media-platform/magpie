using System.Text.Json;

using Magpie.Models.Data;
using Magpie.Models.Filtering;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class CollectionCriteriaSerializationTests
{
    private static readonly JsonSerializerOptions Options = JsonSerializerOptions.Default;

    [Test]
    public void Deserialize_NestedSome_PreservesGroups()
    {
        // Arrange
        const string json = """
        {
            "Relationships": {
                "Some": [
                    [ { "Kind": { "And": [ { "Value": "performer" } ] } } ],
                    [
                        { "Kind": { "And": [ { "Value": "director" } ] } },
                        { "Kind": { "And": [ { "Value": "writer" } ] } }
                    ]
                ],
                "None": []
            }
        }
        """;

        // Act
        var metadata = JsonSerializer.Deserialize<MetadataCriteria>(json, Options);

        // Assert
        Assert.That(metadata, Is.Not.Null);
        var some = metadata!.Relationships.Some;
        Assert.That(some, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(some[0], Has.Count.EqualTo(1));
            Assert.That(some[1], Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Deserialize_EmptySome_YieldsEmptyList()
    {
        // Arrange
        const string json = """{ "Relationships": { "Some": [], "None": [] } }""";

        // Act
        var metadata = JsonSerializer.Deserialize<MetadataCriteria>(json, Options);

        // Assert
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!.Relationships.Some, Is.Empty);
    }

    [Test]
    public void RoundTrip_EmitsNestedShape_AndDeserializesEqual()
    {
        // Arrange
        var original = new CollectionCriteria<RelationshipCriteria>
        {
            Some =
            [
                [new RelationshipCriteria { Kind = new() { And = [new() { Value = "performer" }] } }],
                [new RelationshipCriteria { Kind = new() { And = [new() { Value = "director" }] } }],
            ],
        };

        // Act
        var serialized = JsonSerializer.Serialize(original, Options);

        // Assert
        Assert.That(serialized, Does.Contain("[["));

        var roundTripped = JsonSerializer.Deserialize<CollectionCriteria<RelationshipCriteria>>(serialized, Options);

        Assert.That(roundTripped, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(roundTripped!.Some, Has.Count.EqualTo(2));
            Assert.That(roundTripped.Some[0], Has.Count.EqualTo(1));
            Assert.That(roundTripped.Some[1], Has.Count.EqualTo(1));
        }
    }
}
