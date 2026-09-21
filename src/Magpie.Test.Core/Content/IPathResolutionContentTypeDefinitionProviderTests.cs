using System;
using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class IPathResolutionContentTypeDefinitionProviderTests
{
    private Mock<IPathResolutionContentTypeDefinitionProvider> _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new Mock<IPathResolutionContentTypeDefinitionProvider> { CallBase = true };
        _provider.SetupGet(provider => provider.Definition).Returns(new ContentTypeDefinition()
        {
            Slug = "legacy",
            DisplayName = "Legacy",
            LevelDefinitions =
            [
                new() { Slug = "movie", DisplayName = "Movie", Subfolder = "movies", CustomMetadataFields = [] },
                new() { Slug = "artist", DisplayName = "Artist", Subfolder = string.Empty, CustomMetadataFields = [] },
                new()
                {
                    Slug = "album", ParentSlug = "artist", DisplayName = "Album", CustomMetadataFields = []
                },
                new()
                {
                    Slug = "track", ParentSlug = "album", DisplayName = "Track", CustomMetadataFields = []
                }
            ]
        });
    }

    [Test]
    public void BuildPathPlan_WrapsThePathAtTheLeafLevel_WhenOnlyBuildPathIsImplemented()
    {
        // Arrange
        _provider.Setup(provider => provider.BuildPath(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string?)"The Matrix (1999)/The Matrix.mkv");
        var ancestors = new ContentHierarchyItem[]
        {
            new() { ContentType = "movie", Level = "movie", Slug = "the-matrix", Name = "The Matrix" }
        };

        // Act
        var plan = _provider.Object.BuildPathPlan(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "mkv");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.Directories, Has.Count.EqualTo(1));
            Assert.That(plan.Directories[0].LevelSlug, Is.EqualTo("movie"));
            Assert.That(plan.Directories[0].Name, Is.EqualTo("The Matrix (1999)"));
            Assert.That(plan.FileName, Is.EqualTo("The Matrix.mkv"));
        }
    }

    [Test]
    public void BuildPathPlan_GivesTheChainToTheOwningLevel_WhenTheLegacyPathHasManySegments()
    {
        // Arrange
        _provider.Setup(provider => provider.BuildPath(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string?)"Pink Floyd/The Wall/01 - In the Flesh.flac");
        var ancestors = new ContentHierarchyItem[]
        {
            new() { ContentType = "music", Level = "artist", Slug = "pink-floyd", Name = "Pink Floyd" },
            new() { ContentType = "music", Level = "album", Slug = "the-wall", Name = "The Wall" },
            new() { ContentType = "music", Level = "track", Slug = "in-the-flesh", Name = "In the Flesh" }
        };

        // Act
        var plan = _provider.Object.BuildPathPlan(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "flac");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.Directories, Has.Count.EqualTo(1));
            Assert.That(plan.Directories[0].LevelSlug, Is.EqualTo("artist"));
            Assert.That(plan.Directories[0].Name, Is.EqualTo("Pink Floyd/The Wall"));
            Assert.That(plan.FileName, Is.EqualTo("01 - In the Flesh.flac"));
        }
    }

    [Test]
    public void BuildPathPlan_ReturnsNull_WhenBuildPathReturnsNull()
    {
        // Arrange
        _provider.Setup(provider => provider.BuildPath(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string?)null);
        var ancestors = new ContentHierarchyItem[]
        {
            new() { ContentType = "movie", Level = "movie", Slug = "the-matrix", Name = "The Matrix" }
        };

        // Act
        var plan = _provider.Object.BuildPathPlan(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "mkv");

        // Assert
        Assert.That(plan, Is.Null);
    }

    [Test]
    public void BuildPathPlan_ReturnsNull_WhenTheAncestorChainIsEmpty()
    {
        // Arrange
        _provider.Setup(provider => provider.BuildPath(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string?)null);

        // Act
        var plan = _provider.Object.BuildPathPlan(
            [], new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "mkv");

        // Assert
        Assert.That(plan, Is.Null);
    }

    [Test]
    public void ResolveDirectoryLevelChain_ReturnsNull_WhenParentSlugsFormACycle()
    {
        // Arrange
        _provider.SetupGet(provider => provider.Definition).Returns(new ContentTypeDefinition()
        {
            Slug = "cyclic",
            DisplayName = "Cyclic",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode",
                    ParentSlug = "season",
                    DisplayName = "Episode",
                    CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "season",
                    ParentSlug = "episode",
                    DisplayName = "Season",
                    CustomMetadataFields = []
                }
            ]
        });

        // Act
        var chain = _provider.Object.ResolveDirectoryLevelChain("episode");

        // Assert
        Assert.That(chain, Is.Null);
    }

    [Test]
    public void ResolveDirectoryLevelChain_OmitsALevel_WhenTheLevelOwnsNoDirectory()
    {
        // Arrange
        _provider.SetupGet(provider => provider.Definition).Returns(new ContentTypeDefinition()
        {
            Slug = "flat",
            DisplayName = "Flat",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "show",
                    ParentSlug = null,
                    DisplayName = "Show",
                    Subfolder = string.Empty,
                    CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "season",
                    ParentSlug = "show",
                    DisplayName = "Season",
                    CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode",
                    ParentSlug = "season",
                    DisplayName = "Episode",
                    CustomMetadataFields = []
                }
            ]
        });

        // Act
        var chain = _provider.Object.ResolveDirectoryLevelChain("episode");

        // Assert
        Assert.That(chain, Is.EqualTo(["show"]));
    }

    [Test]
    public void ResolveDirectoryLevelChain_ReturnsAnEmptyChain_WhenNoAncestorOwnsADirectory()
    {
        // Arrange
        _provider.SetupGet(provider => provider.Definition).Returns(new ContentTypeDefinition()
        {
            Slug = "flat",
            DisplayName = "Flat",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "show",
                    ParentSlug = null,
                    DisplayName = "Show",
                    CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode",
                    ParentSlug = "show",
                    DisplayName = "Episode",
                    CustomMetadataFields = []
                }
            ]
        });

        // Act
        var chain = _provider.Object.ResolveDirectoryLevelChain("episode");

        // Assert
        Assert.That(chain, Is.Empty);
    }

    [TestCase("The Matrix (1999)/The Matrix.mkv")]
    [TestCase("The Matrix (1999)\\The Matrix.mkv")]
    public void BuildPathPlan_AttributesTheDirectory_WhenTheProviderUsesEitherSeparator(string relativePath)
    {
        // Arrange
        _provider.SetupGet(provider => provider.Definition).Returns(new ContentTypeDefinition()
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    ParentSlug = null,
                    DisplayName = "Movie",
                    Subfolder = string.Empty,
                    CustomMetadataFields = []
                }
            ]
        });
        _provider.Setup(provider => provider.BuildPath(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(relativePath);
        var ancestors = new ContentHierarchyItem[]
        {
            new() { ContentType = "movie", Level = "movie", Slug = "the-matrix", Name = "The Matrix" }
        };

        // Act
        var plan = _provider.Object.BuildPathPlan(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "mkv");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.Directories, Has.Count.EqualTo(1));
            Assert.That(plan.Directories[0].LevelSlug, Is.EqualTo("movie"));
            Assert.That(plan.Directories[0].Name, Is.EqualTo("The Matrix (1999)"));
            Assert.That(plan.FileName, Is.EqualTo("The Matrix.mkv"));
        }
    }
}
