using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Quality;

[TestFixture]
public class QualityServiceTests
{
    private Mock<IQualityTierListRepository> _tierLists = null!;
    private Mock<IQualityTierRepository> _tiers = null!;
    private Mock<IRootFolderRepository> _rootFolders = null!;
    private Mock<IContentTypeRegistry> _contentTypes = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private IQualityService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _tierLists = new Mock<IQualityTierListRepository>(MockBehavior.Strict);
        _tiers = new Mock<IQualityTierRepository>(MockBehavior.Strict);
        _rootFolders = new Mock<IRootFolderRepository>(MockBehavior.Strict);
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _contentTypes = new Mock<IContentTypeRegistry>(MockBehavior.Strict);
        _fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        _subject = new QualityService(
            _tierLists.Object,
            _tiers.Object,
            _rootFolders.Object,
            _contentTypes.Object,
            _fileSystem.Object);
    }

    [Test]
    public async Task ListTierListsAsync_ReturnsAllLists_WhenListsExist()
    {
        // Arrange
        var all = new QualityTierList[] {
            new() { Id = Guid.NewGuid(), Name = "720p", CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "1080p", CreatedAt = DateTime.UtcNow }
        };
        _tierLists.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(all);

        // Act
        var result = await _subject.ListTierListsAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task PatchTierListAsync_SetsTierListId_WhenPatchIsSent()
    {
        // Arrange
        var tierListId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        QualityTierListPatch? received = null;
        _tierLists.Setup(repository => repository.PatchAsync(
            It.IsAny<QualityTierListPatch>(),
            userId,
            It.IsAny<CancellationToken>()))
            .Callback((QualityTierListPatch patch, Guid? _, CancellationToken _) => received = patch)
            .ReturnsAsync(true);

        // Act
        var result = await _subject.PatchTierListAsync(
            tierListId,
            new QualityTierListPatch { Name = new ScalarPatch<string> { Value = "Renamed" } },
            userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Updated, Is.True);
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Id, Is.EqualTo(tierListId));
        }
    }

    [Test]
    public async Task PatchTierListAsync_NormalizesMimePatterns_WhenPatternsAreValid()
    {
        // Arrange
        QualityTierListPatch? received = null;
        _tierLists.Setup(repository => repository.PatchAsync(
                It.IsAny<QualityTierListPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((QualityTierListPatch patch, Guid? _, CancellationToken _) => received = patch)
            .ReturnsAsync(true);
        var patch = new QualityTierListPatch
        {
            MimeTypes = new ScalarPatch<List<string>>
            {
                Value = [" Audio/* ", "audio/*", "application/x-future"]
            }
        };

        // Act
        await _subject.PatchTierListAsync(Guid.NewGuid(), patch, null).ConfigureAwait(false);

        // Assert
        Assert.That(received!.MimeTypes!.Value, Is.EqualTo(["audio/*", "application/x-future"]));
    }

    [Test]
    public void PatchTierListAsync_ThrowsMagpieException_WhenMimeChangeBreaksLevelDefault()
    {
        // Arrange
        var tierListId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Name = "Books",
                    Path = "/books",
                    ContentType = "book",
                    Defaults = new()
                    {
                        ["book"] = new RootFolderDefault
                    {
                            QualityTierListId = tierListId,
                        },
                    },
                },
            ]);
        _contentTypes.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "book",
                DisplayName = "Book",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "book",
                        DisplayName = "Book",
                        AcceptedFileTypes = ["application/epub+zip"],
                        CustomMetadataFields = [],
                    },
                ],
            });
        _fileSystem.Setup(service => service.MatchesAnyFileType(
                "application/epub+zip",
                "application/epub+zip",
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(false);
        _fileSystem.Setup(service => service.MatchesAnyFileType(
                "audio/*",
                "audio/*",
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(false);
        var patch = new QualityTierListPatch
        {
            MimeTypes = new() { Value = ["audio/*"] },
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchTierListAsync(tierListId, patch, null));

        // Assert
        Assert.That(exception!.Message, Does.Contain("level book accepts application/epub+zip"));
    }

    [Test]
    public async Task CreateTierListAsync_NormalizesMimePatterns_WhenPatternsAreValid()
    {
        // Arrange
        QualityTierList? received = null;
        _tierLists.Setup(repository => repository.CreateAsync(
                It.IsAny<QualityTierList>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((QualityTierList tierList, Guid? _, CancellationToken _) => received = tierList)
            .ReturnsAsync((QualityTierList tierList, Guid? _, CancellationToken _) => tierList);
        var tierList = new QualityTierList
        {
            Name = "Mixed",
            MimeTypes = [" Video/* ", "video/*", "APPLICATION/EPUB+ZIP", "application/vnd.unknown"],
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _subject.CreateTierListAsync(tierList, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            received!.MimeTypes,
            Is.EqualTo(["video/*", "application/epub+zip", "application/vnd.unknown"]));
    }

    [TestCase("video")]
    [TestCase("*/mp4")]
    [TestCase("video/m*")]
    [TestCase("video/mp4; charset=utf-8")]
    public async Task CreateTierListAsync_ReturnsValidationError_WhenMimePatternIsInvalid(string pattern)
    {
        // Arrange
        var tierList = new QualityTierList
        {
            Name = "Invalid",
            MimeTypes = [pattern],
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _subject.CreateTierListAsync(tierList, null).ConfigureAwait(false);

        // Assert
        Assert.That(result.ValidationError, Is.EqualTo($"{pattern} is not a valid MIME pattern."));
    }

    [TestCase("video")]
    [TestCase("*/mp4")]
    [TestCase("video/m*")]
    [TestCase("video/mp4; charset=utf-8")]
    public async Task PatchTierListAsync_ReturnsValidationError_WhenMimePatternIsInvalid(string pattern)
    {
        // Arrange
        var patch = new QualityTierListPatch
        {
            MimeTypes = new() { Value = [pattern] }
        };

        // Act
        var result = await _subject.PatchTierListAsync(Guid.NewGuid(), patch, null).ConfigureAwait(false);

        // Assert
        Assert.That(result.ValidationError, Is.EqualTo($"{pattern} is not a valid MIME pattern."));
    }
}
