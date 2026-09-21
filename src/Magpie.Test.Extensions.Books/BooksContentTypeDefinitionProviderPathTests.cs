using System;
using System.Collections.Generic;

using Magpie.Extensions.Books;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Books;

[TestFixture]
public sealed class BooksContentTypeDefinitionProviderPathTests
{
    private IPathResolutionContentTypeDefinitionProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new BooksContentTypeDefinitionProvider();
    }

    [Test]
    public void BuildPath_Result_WhenEbookHasTitleAndYear()
    {
        // Arrange
        ContentHierarchyItem[] ancestors =
        [
            new()
            {
                ContentType = "book",
                Level = Constants.EbookLevelSlug,
                Slug = "the-martian",
                Name = "The Martian",
                MetadataContainer = new()
            }
        ];
        ancestors[0].MetadataContainer!.Contributions.Add(
            new() { ProviderType = "test", PublishDate = new(2011, 1, 1) });

        // Act
        var path = _subject.BuildPath(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "epub");

        // Assert
        Assert.That(path, Is.EqualTo("The Martian (2011).epub"));
    }

    [Test]
    public void BuildPath_Result_WhenAudiobookHasTitleAndYear()
    {
        // Arrange
        ContentHierarchyItem[] ancestors =
        [
            new()
            {
                ContentType = "book",
                Level = Constants.AudiobookLevelSlug,
                Slug = "the-martian",
                Name = "The Martian",
                MetadataContainer = new()
            }
        ];
        ancestors[0].MetadataContainer!.Contributions.Add(
            new() { ProviderType = "test", PublishDate = new(2011, 1, 1) });

        // Act
        var path = _subject.BuildPath(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "m4b");

        // Assert
        Assert.That(path, Is.EqualTo("The Martian (2011)/source.m4b"));
    }

    [Test]
    public void BuildPathPlan_Result_WhenEbookHasTitleAndYear()
    {
        // Arrange
        ContentHierarchyItem[] ancestors =
        [
            new()
            {
                ContentType = "book",
                Level = Constants.EbookLevelSlug,
                Slug = "the-martian",
                Name = "The Martian",
                MetadataContainer = new()
            }
        ];
        ancestors[0].MetadataContainer!.Contributions.Add(
            new() { ProviderType = "test", PublishDate = new(2011, 1, 1) });

        // Act
        var plan = _subject.BuildPathPlan(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "epub");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.Directories, Has.Count.EqualTo(0));
            Assert.That(plan.FileName, Is.EqualTo("The Martian (2011).epub"));
        }
    }

    [Test]
    public void BuildPathPlan_Result_WhenAudiobookHasTitleAndYear()
    {
        // Arrange
        ContentHierarchyItem[] ancestors =
        [
            new()
            {
                ContentType = "book",
                Level = Constants.AudiobookLevelSlug,
                Slug = "the-martian",
                Name = "The Martian",
                MetadataContainer = new()
            }
        ];
        ancestors[0].MetadataContainer!.Contributions.Add(
            new() { ProviderType = "test", PublishDate = new(2011, 1, 1) });

        // Act
        var plan = _subject.BuildPathPlan(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "m4b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.Directories, Has.Count.EqualTo(1));
            Assert.That(plan.Directories[0].LevelSlug, Is.EqualTo("audiobook"));
            Assert.That(plan.Directories[0].Name, Is.EqualTo("The Martian (2011)"));
            Assert.That(plan.FileName, Is.EqualTo("source.m4b"));
        }
    }
}
