using System.Linq;

using Magpie.Core.Content;
using Magpie.Models;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentTypeRegistryTests
{
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    [Test]
    public void GetDefinitionFor_ReturnsTheLastDefinition_WhenSlugsAreDuplicated()
    {
        // Arrange
        var firstDefinition = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A 1",
            LevelDefinitions = []
        };
        var secondDefinition = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A 2",
            LevelDefinitions = []
        };
        var first = Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == firstDefinition);
        var second = Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == secondDefinition);

        // Act
        var registry = new ContentTypeRegistry([first, second], _loggerFactory.CreateLogger<ContentTypeRegistry>());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Definitions, Has.Count.EqualTo(1));
            Assert.That(registry.GetDefinitionFor("type-a")!.DisplayName, Is.EqualTo("Type A 2"));
            Assert.That(registry.FindProviderFor("type-a"), Is.SameAs(second));
        }
    }

    [TestCase("Type-A", "level-a")]
    [TestCase("type-a", "Level-A")]
    public void Constructor_IgnoresTheDefinition_WhenASlugIsNotLowercase(string typeSlug, string levelSlug)
    {
        // Arrange
        var definition = new ContentTypeDefinition
        {
            Slug = typeSlug,
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = levelSlug,
                    ParentSlug = null,
                    DisplayName = "Level A",
                    CustomMetadataFields = []
                }
            ]
        };

        // Act
        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == definition)],
            _loggerFactory.CreateLogger<ContentTypeRegistry>());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Definitions, Is.Empty);
            Assert.That(registry.GetDefinitionFor(typeSlug), Is.Null);
            Assert.That(registry.Exists(typeSlug), Is.False);
        }
    }

    [Test]
    public void FindProviderFor_ReturnsProvider_WhenSlugMatchesExactly()
    {
        // Arrange
        var definition = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions = []
        };
        var provider = Mock.Of<IPathResolutionContentTypeDefinitionProvider>(p => p.Definition == definition);
        var registry = new ContentTypeRegistry(
            [provider],
            _loggerFactory.CreateLogger<ContentTypeRegistry>());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.FindProviderFor("type-a"), Is.SameAs(provider));
            Assert.That(registry.FindProviderFor("TYPE-A"), Is.Null);
        }
    }

    [Test]
    public void Definitions_RetainAllEntries_WhenSlugsAreDistinct()
    {
        // Arrange
        var typeA = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions = []
        };
        var typeC = new ContentTypeDefinition
        {
            Slug = "type-c",
            DisplayName = "Type C",
            LevelDefinitions = []
        };

        // Act
        var registry = new ContentTypeRegistry(
            [
                Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == typeA),
                Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == typeC)
            ],
            _loggerFactory.CreateLogger<ContentTypeRegistry>());

        // Assert
        Assert.That(registry.Definitions.Select(d => d.Slug), Is.EquivalentTo(["type-a", "type-c"]));
    }
}
