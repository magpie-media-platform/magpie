using System.Linq;

using Magpie.Core.Subjects;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Subjects;

[TestFixture]
public sealed class BuiltInSubjectKindDefinitionProviderTests
{
    [Test]
    public void GetSubjectKindDefinitions_ReturnsFallbackIdentityFields_WhenKindIsPerson()
    {
        // Arrange
        var provider = new BuiltInSubjectKindDefinitionProvider();

        // Act
        var definition = provider.GetSubjectKindDefinitions().Single(d => d.Slug == "person");

        // Assert
        Assert.That(
            definition.FallbackIdentityFields,
            Is.EqualTo(MetadataField.Title | MetadataField.BirthDate));
    }

    [Test]
    public void GetSubjectKindDefinitions_ReturnsStudioDefinition_WhenDefinitionsAreEnumerated()
    {
        // Arrange
        var provider = new BuiltInSubjectKindDefinitionProvider();

        // Act
        var definition = provider.GetSubjectKindDefinitions().Single(d => d.Slug == "studio");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.DisplayName, Is.EqualTo("Studio"));
            Assert.That(definition.PluralDisplayName, Is.EqualTo("Studios"));
            Assert.That(definition.IconName, Is.EqualTo("buildings"));
        }
    }

    [Test]
    public void Definitions_ContainPersonAndStudio_WhenNoExtensionProvidersAreRegistered()
    {
        // Arrange
        var registry = new SubjectKindRegistry(
            [new BuiltInSubjectKindDefinitionProvider()],
            NullLogger<SubjectKindRegistry>.Instance);

        // Act
        var definitionCount = registry.Definitions.Count;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Contains("person"), Is.True);
            Assert.That(registry.Contains("studio"), Is.True);
            Assert.That(definitionCount, Is.EqualTo(2));
        }
    }

    [Test]
    public void GetSubjectKindDefinitions_DeclaresEmittedPersonRoles_WhenKindIsPerson()
    {
        // Arrange
        var provider = new BuiltInSubjectKindDefinitionProvider();

        // Act
        var roles = provider.GetSubjectKindDefinitions().Single(definition => definition.Slug == "person")
            .RelationshipRoles.Select(role => role.Slug);

        // Assert
        string[] expected =
        [
            "cast", "guest-cast", "director", "writer", "crew", "author", "translator", "illustrator", "editor",
            "contributor", "artist", "performer"
        ];
        Assert.That(roles, Is.EqualTo(expected));
    }

    [Test]
    public void GetSubjectKindDefinitions_DeclaresEmittedStudioRoles_WhenKindIsStudio()
    {
        // Arrange
        var provider = new BuiltInSubjectKindDefinitionProvider();

        // Act
        var roles = provider.GetSubjectKindDefinitions().Single(definition => definition.Slug == "studio")
            .RelationshipRoles.Select(role => role.Slug);

        // Assert
        Assert.That(roles, Is.EqualTo((string[])["studio", "network"]));
    }
}
