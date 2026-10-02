using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Quality;

[TestFixture]
public sealed class TierListLevelCompatibilityTests
{
    private static readonly Guid TierListId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid RootFolderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly ContentHierarchyLevelDefinition EbookByMimeType = new()
    {
        Slug = "ebook",
        DisplayName = "E-book",
        CustomMetadataFields = [],
        AcceptedFileTypes = ["application/epub+zip"]
    };

    private static readonly ContentHierarchyLevelDefinition EbookByExtension = new()
    {
        Slug = "ebook",
        DisplayName = "E-book",
        CustomMetadataFields = [],
        AcceptedFileTypes = [".epub"]
    };

    private static readonly ContentHierarchyLevelDefinition EbookByUnmappedExtension = new()
    {
        Slug = "ebook",
        DisplayName = "E-book",
        CustomMetadataFields = [],
        AcceptedFileTypes = [".unmapped"]
    };

    private static readonly ContentHierarchyLevelDefinition Movie = new()
    {
        Slug = "movie",
        DisplayName = "Movie",
        CustomMetadataFields = [],
        AcceptedFileTypes = ["video/*"]
    };

    private static readonly ContentHierarchyLevelDefinition Empty = new()
    {
        Slug = "empty",
        DisplayName = "Empty",
        CustomMetadataFields = []
    };

    private static readonly ContentHierarchyLevelDefinition Series = new()
    {
        Slug = "series",
        DisplayName = "Series",
        CustomMetadataFields = []
    };

    private static readonly ContentHierarchyLevelDefinition Episode = new()
    {
        Slug = "episode",
        DisplayName = "Episode",
        ParentSlug = "series",
        CustomMetadataFields = [],
        AcceptedFileTypes = ["video/*"]
    };

    private static readonly ContentHierarchyLevelDefinition Audio = new()
    {
        Slug = "audio",
        DisplayName = "Audio",
        CustomMetadataFields = [],
        AcceptedFileTypes = ["audio/*"]
    };

    private Mock<IContentTypeRegistry> _contentTypes = null!;
    private Mock<IQualityTierListRepository> _tierLists = null!;
    private Mock<IRootFolderRepository> _rootFolders = null!;
    private MemoryCacheStore _store = null!;
    private MemoryCacheFactory _cacheFactory = null!;
    private IQualityService _quality = null!;
    private IRootFolderService _subject = null!;

    public static IEnumerable<TestCaseData> CompatibleCases =>
    [
        new(new[] { EbookByMimeType }, "ebook", new[] { "application/epub+zip" }),
        new(new[] { EbookByMimeType }, "ebook", Array.Empty<string>()),
        new(new[] { EbookByExtension }, "ebook", new[] { "application/epub+zip" }),
        new(new[] { Movie }, "movie", new[] { "video/mp4" }),
        new(new[] { EbookByMimeType }, "ebook", new[] { "application/*" }),
        new(new[] { Empty }, "empty", Array.Empty<string>()),
        new(new[] { Series, Episode }, "series", new[] { "video/*" }),
    ];

    public static IEnumerable<TestCaseData> IncompatibleCases =>
    [
        new(new[] { EbookByMimeType }, "ebook", new[] { "video/*" }),
        new(new[] { EbookByExtension }, "ebook", new[] { "video/*" }),
        new(new[] { EbookByUnmappedExtension }, "ebook", new[] { "video/*" }),
        new(new[] { Empty }, "empty", new[] { "video/*" }),
        new(new[] { Series, Episode, Audio }, "series", new[] { "audio/*" }),
    ];

    [SetUp]
    public void SetUp()
    {
        _contentTypes = new Mock<IContentTypeRegistry>();
        _contentTypes.Setup(registry => registry.Exists("book")).Returns(true);
        _tierLists = new Mock<IQualityTierListRepository>();
        _tierLists
            .Setup(repository => repository.PatchAsync(
                It.IsAny<QualityTierListPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _rootFolders = new Mock<IRootFolderRepository>();
        _rootFolders
            .Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var mimeMappings = new Mock<IMimeMappingRepository>();
        mimeMappings
            .Setup(repository => repository.GetByIdAsync(".epub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MimeMapping { Extension = ".epub", MimeType = "application/epub+zip" });
        _store = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_store);
        var fileSystem = new FileSystem(
            Mock.Of<IBlockedFileTypeRepository>(),
            mimeMappings.Object,
            _cacheFactory,
            NullLogger<FileSystem>.Instance);
        _quality = new QualityService(
            _tierLists.Object,
            Mock.Of<IQualityTierRepository>(),
            _rootFolders.Object,
            _contentTypes.Object,
            fileSystem);
        _subject = new RootFolderService(_rootFolders.Object, fileSystem, _contentTypes.Object, _quality);
    }

    [TearDown]
    public void TearDown()
    {
        _cacheFactory.Dispose();
        _store.Dispose();
    }

    [TestCaseSource(nameof(CompatibleCases))]
    public async Task PatchAsync_SavesTheDefault_WhenTierListIsCompatibleWithLevel(
        ContentHierarchyLevelDefinition[] levels,
        string levelSlug,
        string[] mimeTypes)
    {
        // Arrange
        _contentTypes.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition { Slug = "book", DisplayName = "Book", LevelDefinitions = levels });
        _tierLists.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new QualityTierList { Id = TierListId, Name = "Candidate", MimeTypes = [.. mimeTypes] }]);
        _rootFolders.Setup(repository => repository.GetByIdAsync(RootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder { Id = RootFolderId, Name = "Books", Path = "/books", ContentType = "book" });
        var patch = new RootFolderPatch
        {
            Defaults = new() { Value = new() { [levelSlug] = new() { QualityTierListId = TierListId } } }
        };

        // Act
        var patched = await _subject.PatchAsync(RootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        Assert.That(patched, Is.True);
    }

    [TestCaseSource(nameof(IncompatibleCases))]
    public void PatchAsync_Throws_WhenTierListIsIncompatibleWithLevel(
        ContentHierarchyLevelDefinition[] levels,
        string levelSlug,
        string[] mimeTypes)
    {
        // Arrange
        _contentTypes.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition { Slug = "book", DisplayName = "Book", LevelDefinitions = levels });
        _tierLists.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new QualityTierList { Id = TierListId, Name = "Candidate", MimeTypes = [.. mimeTypes] }]);
        _rootFolders.Setup(repository => repository.GetByIdAsync(RootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder { Id = RootFolderId, Name = "Books", Path = "/books", ContentType = "book" });
        var patch = new RootFolderPatch
        {
            Defaults = new() { Value = new() { [levelSlug] = new() { QualityTierListId = TierListId } } }
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.PatchAsync(RootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                exception!.Message,
                Does.StartWith($"Quality tier list Candidate ({TierListId}) is not compatible with level ")
                    .And.Contain($"({levelSlug})."));
            _rootFolders.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<RootFolderPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCaseSource(nameof(CompatibleCases))]
    public async Task PatchTierListAsync_Succeeds_WhenTierListIsCompatibleWithLevel(
        ContentHierarchyLevelDefinition[] levels,
        string levelSlug,
        string[] mimeTypes)
    {
        // Arrange
        _contentTypes.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition { Slug = "book", DisplayName = "Book", LevelDefinitions = levels });
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = RootFolderId,
                    Name = "Books",
                    Path = "/books",
                    ContentType = "book",
                    Defaults = new() { [levelSlug] = new() { QualityTierListId = TierListId } }
                }
            ]);
        var patch = new QualityTierListPatch { MimeTypes = new ScalarPatch<List<string>> { Value = [.. mimeTypes] } };

        // Act
        var result = await _quality.PatchTierListAsync(TierListId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ValidationError, Is.Null);
            Assert.That(result.Updated, Is.True);
        }
    }

    [TestCaseSource(nameof(IncompatibleCases))]
    public async Task PatchTierListAsync_ReturnsValidationError_WhenTierListIsIncompatibleWithLevel(
        ContentHierarchyLevelDefinition[] levels,
        string levelSlug,
        string[] mimeTypes)
    {
        // Arrange
        _contentTypes.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition { Slug = "book", DisplayName = "Book", LevelDefinitions = levels });
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = RootFolderId,
                    Name = "Books",
                    Path = "/books",
                    ContentType = "book",
                    Defaults = new() { [levelSlug] = new() { QualityTierListId = TierListId } }
                }
            ]);
        var patch = new QualityTierListPatch { MimeTypes = new ScalarPatch<List<string>> { Value = [.. mimeTypes] } };

        // Act
        var result = await _quality.PatchTierListAsync(TierListId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ValidationError, Does.Contain($"Books level {levelSlug} accepts"));
            Assert.That(result.Updated, Is.False);
        }
    }
}
