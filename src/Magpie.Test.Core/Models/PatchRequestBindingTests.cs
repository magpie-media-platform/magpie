using System.Text.Json;

using Magpie.Models.Arguments;

using NUnit.Framework;

namespace Magpie.Test.Core.Models;

[TestFixture]
public sealed class PatchRequestBindingTests
{
    [Test]
    public void Deserialize_LeavesTheFieldUnset_WhenAnExtensionPatchBodyNamesDisabledReason()
    {
        // Arrange
        var body = """{"enabled":{"value":true},"disabledReason":{"value":"Activation failed"}}""";

        // Act
        var patch = JsonSerializer.Deserialize<ExtensionPatch>(body, JsonSerializerOptions.Web);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch!.Enabled!.Value, Is.True);
            Assert.That(patch.DisabledReason, Is.Null);
        }
    }

    [Test]
    public void Deserialize_LeavesTheFieldUnset_WhenAContentPatchBodyNamesAServerMaintainedField()
    {
        // Arrange
        var body = """
            {"monitoring":{"value":true},"name":{"value":"Renamed"},"slug":{"value":"stolen-slug"},
            "settings":{"add":{"content.path.origin":"Manual"}},"dateLastRefreshed":{"value":"2026-01-01T00:00:00Z"}}
            """;

        // Act
        var patch = JsonSerializer.Deserialize<ContentHierarchyItemPatch>(body, JsonSerializerOptions.Web);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch!.Monitoring!.Value, Is.True);
            Assert.That(patch.Name, Is.Null);
            Assert.That(patch.Slug, Is.Null);
            Assert.That(patch.Settings, Is.Null);
            Assert.That(patch.DateLastRefreshed, Is.Null);
        }
    }

    [Test]
    public void Deserialize_LeavesTheFieldUnset_WhenAMetadataContainerPatchBodyNamesDateLastRefreshed()
    {
        // Arrange
        var body = """{"title":{"value":"Renamed"},"dateLastRefreshed":{"value":"2026-01-01T00:00:00Z"}}""";

        // Act
        var patch = JsonSerializer.Deserialize<MetadataContainerPatch>(body, JsonSerializerOptions.Web);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch!.Title!.Value, Is.EqualTo("Renamed"));
            Assert.That(patch.DateLastRefreshed, Is.Null);
        }
    }

    [Test]
    public void Deserialize_LeavesTheFieldUnset_WhenAScheduleTriggerPatchBodyNamesNextRunAt()
    {
        // Arrange
        var body = """{"enabled":{"value":true},"nextRunAt":{"value":"2026-01-01T00:00:00Z"}}""";

        // Act
        var patch = JsonSerializer.Deserialize<ScheduleTriggerPatch>(body, JsonSerializerOptions.Web);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch!.Enabled!.Value, Is.True);
            Assert.That(patch.NextRunAt, Is.Null);
        }
    }
}
