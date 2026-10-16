using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Core.Subjects;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentFileServiceTests
{
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItems = null!;
    private Mock<IMetadataContainerRepository> _metadataContainers = null!;
    private Mock<IRootFolderRepository> _rootFolders = null!;
    private Mock<IContentTypeRegistry> _contentTypes = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IMediaFileRepository> _mediaFiles = null!;
    private Mock<IContentRepository> _contentItems = null!;
    private Mock<IAuditRecordRepository> _auditRecords = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private Dictionary<string, long> _files = null!;
    private RootFolder _rootFolder = null!;
    private ContentHierarchyItem _leaf = null!;
    private IContentFileService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _files = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        _rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Movies",
            Path = Path.Combine(Path.GetTempPath(), "content-file-rename", Guid.NewGuid().ToString("N")),
            ContentType = "movie",
            RenameTemplate = null,
            CreatedAt = DateTime.UtcNow
        };
        _leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Slug = "the-matrix",
            Name = "The Matrix",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow }
        };
        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _leaf);
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [_leaf]);
        _metadataContainers = new Mock<IMetadataContainerRepository> { CallBase = true };
        _metadataContainers
            .Setup(r => r.GetRelationshipTitlesByContentHierarchyItemAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>());
        _rootFolders = new Mock<IRootFolderRepository> { CallBase = true };
        _rootFolders
            .Setup(r => r.GetByIdAsync(_rootFolder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rootFolder);
        _contentTypes = new Mock<IContentTypeRegistry>();
        _contentTypes
            .Setup(r => r.FindProviderFor(_leaf.ContentType))
            .Returns((IContentTypeDefinitionProvider?)null);
        _fileSystem = new Mock<IFileSystem>();
        _fileSystem
            .Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => _files.ContainsKey(path));
        _fileSystem
            .Setup(f => f.GetFileSize(It.IsAny<string>()))
            .Returns((string path) => _files[path]);
        _fileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.GetFullPath(It.IsAny<string>()))
            .Returns((string path) => Path.GetFullPath(path));
        _fileSystem.Setup(f => f.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => Path.GetRelativePath(left, right) == ".");
        _fileSystem.Setup(f => f.IsPathWithin(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string root, string path) =>
            {
                var relative = Path.GetRelativePath(root, path);
                return !Path.IsPathRooted(relative) && relative != ".." &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            });
        _fileSystem.Setup(f => f.PathContainsLink(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        _fileSystem
            .Setup(f => f.Move(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination, bool _) =>
            {
                var fileSize = _files[source];
                _files.Remove(source);
                _files[destination] = fileSize;
            });

        _mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        _contentItems = new Mock<IContentRepository> { CallBase = true };
        _auditRecords = new Mock<IAuditRecordRepository> { CallBase = true };
        _transactionManager = new Mock<ITransactionManager>();
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<MediaFile>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<MediaFile>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<AttachContentFileResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<AttachContentFileResult>> operation,
                CancellationToken cancellationToken) => operation(cancellationToken));
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        _subject = new ContentFileService(
            _contentHierarchyItems.Object,
            _metadataContainers.Object,
            _rootFolders.Object,
            _contentTypes.Object,
            new SubjectKindRegistry([], NullLogger<SubjectKindRegistry>.Instance),
            _fileSystem.Object,
            _mediaFiles.Object,
            _contentItems.Object,
            _auditRecords.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<ContentFileService>.Instance);
    }

    [TestCaseSource(typeof(ContentFileServiceTemplateCases), nameof(ContentFileServiceTemplateCases.Cases))]
    public async Task PreviewRenameAsync_ReturnsExpectedPath_WhenTokensAreRendered(
        string template,
        string title,
        string? originalTitle,
        DateTime? publishDate,
        string fileName,
        string[] expectedSegments)
    {
        // Arrange
        _rootFolder.RenameTemplate = template;
        _leaf.Name = title;
        _leaf.MetadataContainer!.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            OriginalTitle = originalTitle,
            PublishDate = publishDate,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", fileName));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(result.Rename.FullName, Is.EqualTo(Path.Combine([_rootFolder.Path, .. expectedSegments])));
    }

    [Test]
    public async Task PreviewRenameAsync_ReturnsRootFirstProviderPath_WhenMetadataAndRelationshipsExist()
    {
        // Arrange
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "test",
            Level = "series",
            Slug = "root",
            Name = "Root",
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow }
        };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "test",
            Level = "season",
            Slug = "leaf",
            Name = "Leaf",
            PendingMetadataRefresh = true,
            Ordinal = 2,
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow }
        };
        leaf.MetadataContainer!.Contributions.Add(new MetadataContribution
        {
            Ordinal = 2,
            ProviderType = "second",
            Title = "Later",
            PublishDate = new DateTime(2021, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        leaf.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 1,
            ProviderType = "first",
            Title = "Metadata Leaf",
            PublishDate = new DateTime(2020, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        var relationships = new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>
        {
            [leaf.Id] = new Dictionary<string, IReadOnlyList<string>> { ["network"] = ["Network"] }
        };
        var provider = new Mock<IPathResolutionContentTypeDefinitionProvider>();
        provider
            .Setup(p => p.BuildPathPlan(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns((IReadOnlyList<ContentHierarchyItem> ancestors,
                IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
                string originalFileName, string extension) =>
            {
                var year = ancestors[^1].MetadataContainer!.Contributions.OrderBy(c => c.Ordinal)
                    .First(c => c.PublishDate is not null).PublishDate!.Value.Year;
                return new ContentPathPlan
                {
                    Directories =
                    [
                        new ContentPathPlanDirectory { LevelSlug = ancestors[0].Level, Name = ancestors[0].Name },
                        new ContentPathPlanDirectory
                        {
                            LevelSlug = ancestors[^1].Level,
                            Name = $"{ancestors[^1].Name} ({year}) " +
                                $"{relationshipsByNode[ancestors[^1].Id]["network"][0]}"
                        }
                    ],
                    FileName = $"{originalFileName}.{extension}"
                };
            });
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([leaf, root]);
        _metadataContainers
            .Setup(repository => repository.GetRelationshipTitlesByContentHierarchyItemAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationships);
        _contentTypes.Setup(registry => registry.FindProviderFor("test")).Returns(provider.Object);
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Rename.FullName,
            Is.EqualTo(Path.Combine(_rootFolder.Path, "Root", "Metadata Leaf (2020) Network", "source.mkv")));
    }

    [TestCase("AC/DC", "01 - Hells Bells.flac", "AC DC", "01 - Hells Bells.flac")]
    [TestCase("AC\\DC", "01 - Hells Bells.flac", "AC DC", "01 - Hells Bells.flac")]
    [TestCase("Show", "Show - S01E05 - 24/7.mkv", "Show", "Show - S01E05 - 24 7.mkv")]
    public async Task PreviewRenameAsync_KeepsEachNameInOneSegment_WhenAProviderNameContainsAPathSeparator(
        string directoryName,
        string fileName,
        string expectedDirectory,
        string expectedFileName)
    {
        // Arrange
        var provider = new Mock<IPathResolutionContentTypeDefinitionProvider>();
        provider
            .Setup(p => p.BuildPathPlan(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(new ContentPathPlan
            {
                Directories = [new ContentPathPlanDirectory { LevelSlug = "movie", Name = directoryName }],
                FileName = fileName
            });
        _contentTypes.Setup(registry => registry.FindProviderFor("movie")).Returns(provider.Object);
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Rename.FullName,
            Is.EqualTo(Path.Combine(_rootFolder.Path, expectedDirectory, expectedFileName)));
    }

    [Test]
    public async Task PreviewRenameAsync_KeepsTheItemNameInOneSegment_WhenTheNameContainsAPathSeparator()
    {
        // Arrange
        _leaf.Name = "Face/Off";
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(result.Rename.FullName, Is.EqualTo(Path.Combine(_rootFolder.Path, "Face Off", "source.mkv")));
    }

    [Test]
    public async Task PreviewRenameAsync_ReturnsSubfolderTarget_WhenRootLevelDeclaresSubfolder()
    {
        // Arrange
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "movie",
                        DisplayName = "Movie",
                        CustomMetadataFields = [],
                        Subfolder = "movies"
                    }
                ]
            });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Rename.FullName,
            Is.EqualTo(Path.Combine(_rootFolder.Path, "movies", "The Matrix", "source.mkv")));
    }

    [Test]
    public async Task PreviewRenameAsync_ReturnsFlatTarget_WhenRootLevelHasNoSubfolder()
    {
        // Arrange
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "movie",
                        DisplayName = "Movie",
                        CustomMetadataFields = []
                    }
                ]
            });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Rename.FullName,
            Is.EqualTo(Path.Combine(_rootFolder.Path, "The Matrix", "source.mkv")));
    }

    [Test]
    public async Task PreviewRenameAsync_ReturnsAbsoluteItemPathWithoutRebasing_WhenRootLevelDeclaresSubfolder()
    {
        // Arrange
        var absolutePath = Path.Combine(Path.GetTempPath(), "outside-library", Guid.NewGuid().ToString("N"));
        _leaf.Path = absolutePath;
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "movie",
                        DisplayName = "Movie",
                        CustomMetadataFields = [],
                        Subfolder = "movies"
                    }
                ]
            });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(result.Rename.FullName, Is.EqualTo(Path.Combine(absolutePath, "source.mkv")));
    }

    [Test]
    public async Task ExecuteRenameAsync_DoesNotPersistGeneratedPath_WhenRootLevelDeclaresSubfolder()
    {
        // Arrange
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "movie",
                        DisplayName = "Movie",
                        CustomMetadataFields = [],
                        Subfolder = "movies"
                    }
                ]
            });
        var originalPath = Path.Combine(_rootFolder.Path, "incoming", "source.mkv");
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = originalPath
        };
        _mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.FilePaths!.Contains(originalPath)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [mediaFile] });
        _mediaFiles
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _files[originalPath] = 100;
        var preview = await _subject.PreviewRenameAsync(_leaf.Id, new FileInfo(originalPath)).ConfigureAwait(false);

        // Act
        await _subject.ExecuteRenameAsync(preview, userId: null, correlationId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                preview.Rename.FullName,
                Is.EqualTo(Path.Combine(_rootFolder.Path, "movies", "The Matrix", "source.mkv")));
            Assert.That(_leaf.Path, Is.Null);
        }

        _contentHierarchyItems.Verify(
            repository => repository.UpdateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task PreviewRenameAsync_UsesTemplate_WhenProviderThrows()
    {
        // Arrange
        _rootFolder.RenameTemplate = "{Title} ({Year})/{OriginalTitle}.{Ext}";
        _leaf.MetadataContainer!.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            OriginalTitle = "Matrix, The",
            PublishDate = new DateTime(1999, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        var provider = new Mock<IPathResolutionContentTypeDefinitionProvider>();
        provider
            .Setup(p => p.BuildPathPlan(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws<InvalidOperationException>();
        _contentTypes.Setup(registry => registry.FindProviderFor(_leaf.ContentType)).Returns(provider.Object);
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Rename.FullName,
            Is.EqualTo(Path.Combine(_rootFolder.Path, "The Matrix (1999)", "Matrix, The.mkv")));
    }

    [Test]
    public async Task PreviewRenameAsync_ReturnsTemplateTarget_WhenMetadataProvidesTokens()
    {
        // Arrange
        _rootFolder.RenameTemplate = "{Title} ({Year}).{Ext}";
        _leaf.MetadataContainer!.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            PublishDate = new DateTime(1999, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "raw.mkv"));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Original.FullName, Is.EqualTo(original.FullName));
            Assert.That(result.Rename.FullName, Is.EqualTo(Path.Combine(_rootFolder.Path, "The Matrix (1999).mkv")));
            Assert.That(result.Item, Is.SameAs(_leaf));
        }
    }

    [Test]
    public async Task PreviewRenameAsync_NestsMaskFoldersUnderTheExistingFolder_WhenTheUserCreatedIt()
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            Path = "Show",
            RootFolderId = _rootFolder.Id,
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var primary = new MediaFile
        {
            Id = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = Path.Combine(_rootFolder.Path, "Show", "old.mkv")
        };
        _rootFolder.RenameTemplate = "Extras/{Title}.{Ext}";
        _leaf.ContentType = "tv";
        _leaf.Level = "episode";
        _leaf.Name = "Episode";
        _leaf.ParentId = show.Id;
        _leaf.ContentItem = new ContentItem { MediaFiles = { primary } };
        _files[primary.FilePath] = 5;
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_leaf, show]);
        _contentHierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.AncestorId == _leaf.Id),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "show",
                        DisplayName = "Show",
                        CustomMetadataFields = [],
                        Subfolder = string.Empty
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "episode",
                        DisplayName = "Episode",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = [".mkv"]
                    }
                ]
            });

        // Act
        var placement = await _subject.PreviewRenameAsync(_leaf.Id, new FileInfo(primary.FilePath))
            .ConfigureAwait(false);
        var rename = await _subject.PreviewRenameMaskAsync(_leaf.Id, new() { MediaFileIds = [primary.Id] })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                placement.Rename.FullName,
                Is.EqualTo(Path.Combine(_rootFolder.Path, "Show", "Extras", "Episode.mkv")));
            Assert.That(rename.Files.Single().DestinationPath, Is.EqualTo(placement.Rename.FullName));
        }
    }

    [Test]
    public async Task PreviewRenameAsync_NestsMaskFoldersUnderTheGeneratedFolder_WhenTheFolderIsNew()
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            RootFolderId = _rootFolder.Id,
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            Settings = { ["content.path.origin"] = "Generated" }
        };
        _rootFolder.RenameTemplate = "Extras/{Title}.{Ext}";
        _leaf.ContentType = "tv";
        _leaf.Level = "episode";
        _leaf.Name = "Episode";
        _leaf.ParentId = show.Id;
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_leaf, show]);
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "show",
                        DisplayName = "Show",
                        CustomMetadataFields = [],
                        Subfolder = string.Empty
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "episode",
                        DisplayName = "Episode",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = [".mkv"]
                    }
                ]
            });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "old.mkv"));

        // Act
        var placement = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        Assert.That(
            placement.Rename.FullName,
            Is.EqualTo(Path.Combine(_rootFolder.Path, "Episode", "Extras", "Episode.mkv")));
    }

    [Test]
    public void PreviewRenameAsync_Throws_WhenRootSubfolderEscapesTheRootFolder()
    {
        // Arrange
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movies",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "movie",
                        ParentSlug = null,
                        DisplayName = "Movie",
                        CustomMetadataFields = [],
                        Subfolder = "../outside"
                    }
                ]
            });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "raw.mkv"));

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => _subject.PreviewRenameAsync(_leaf.Id, original));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Rename target escapes root folder"));
    }

    [TestCase(new string[0], 1, 1, 2, 3, "S01E01.mkv")]
    [TestCase(
        new[] { "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333" },
        1,
        1,
        2,
        3,
        "S01E01-03.mkv")]
    [TestCase(new string[0], 17, 5, 6, 7, "S17E05.mkv")]
    [TestCase(
        new[] { "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333" },
        17,
        5,
        6,
        7,
        "S17E05-07.mkv")]
    [TestCase(new string[0], 0, 5, 6, 7, "S00E05.mkv")]
    public async Task PreviewRenameMaskAsync_RendersTheCoveredRange_WhenTheFileCoversSiblings(
        string[] covered,
        int seasonNumber,
        int firstNumber,
        int secondNumber,
        int thirdNumber,
        string expectedFileName)
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            Path = "Show",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var primary = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid("11111111-1111-1111-1111-111111111111"),
            FileKind = "Video",
            FilePath = Path.Combine(_rootFolder.Path, "Show", "old.mkv")
        };
        foreach (var contentItemId in covered)
        {
            primary.Coverage.Add(new() { MediaFileId = primary.Id, ContentItemId = new Guid(contentItemId) });
        }

        _rootFolder.RenameTemplate = "S{Season.Number:00}E{Episode.Number:00}.{Ext}";
        _leaf.ContentType = "tv";
        _leaf.Level = "episode";
        _leaf.Ordinal = 1;
        _leaf.ParentId = season.Id;
        _leaf.ContentItem = new ContentItem { Id = primary.ContentItemId, MediaFiles = { primary } };
        season.MetadataContainer = new()
        {
            Contributions =
            {
                new() { ProviderType = "tmdb", Ordinal = 0, SequenceNumber = 1 },
                new() { ProviderType = "user", Ordinal = -1, SequenceNumber = seasonNumber }
            }
        };
        _leaf.MetadataContainer = new()
        {
            Contributions =
            {
                new() { ProviderType = "tmdb", Ordinal = 0, SequenceNumber = 1 },
                new() { ProviderType = "user", Ordinal = -1, SequenceNumber = firstNumber }
            }
        };
        _files[primary.FilePath] = 5;
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_leaf, season, show]);
        _contentHierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.AncestorId == _leaf.Id),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        _contentHierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria =>
                    criteria.ParentId.And.Any(criterion => criterion.Value == season.Id)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    _leaf,
                    new()
                    {
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "episode-2",
                        Name = "Episode 2",
                        Ordinal = 2,
                        MetadataContainer = new()
                        {
                            Contributions = { new() { ProviderType = "user", SequenceNumber = secondNumber } }
                        },
                        ContentItem = new() { Id = new Guid("22222222-2222-2222-2222-222222222222") }
                    },
                    new()
                    {
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "episode-3",
                        Name = "Episode 3",
                        Ordinal = 3,
                        MetadataContainer = new()
                        {
                            Contributions = { new() { ProviderType = "user", SequenceNumber = thirdNumber } }
                        },
                        ContentItem = new() { Id = new Guid("33333333-3333-3333-3333-333333333333") }
                    }
                ],
                TotalItems = 3
            });
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "show",
                        DisplayName = "Show",
                        CustomMetadataFields = [],
                        Subfolder = string.Empty
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "season",
                        DisplayName = "Season",
                        CustomMetadataFields = []
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "episode",
                        DisplayName = "Episode",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = [".mkv"]
                    }
                ]
            });

        // Act
        var rename = await _subject.PreviewRenameMaskAsync(_leaf.Id, new() { MediaFileIds = [primary.Id] })
            .ConfigureAwait(false);

        // Assert
        Assert.That(
            rename.Files.Single().DestinationPath,
            Is.EqualTo(Path.Combine(_rootFolder.Path, "Show", expectedFileName)));
    }

    [TestCase(1, 1, 2, 3, "S01E01-03.mkv", null)]
    [TestCase(17, 5, 6, 7, "S17E05-07.mkv", null)]
    [TestCase(17, 7, 5, 6, "S17E05-07.mkv", null)]
    [TestCase(17, 5, 7, 8, null, "The rename token 'Episode.Number' has no value.")]
    [TestCase(17, 5, 6, 8, null, "The rename token 'Episode.Number' has no value.")]
    [TestCase(17, 5, 5, 6, null, "The rename token 'Episode.Number' has no value.")]
    [TestCase(17, 5, 5, 5, null, "The rename token 'Episode.Number' has no value.")]
    [TestCase(null, 5, 6, 7, null, "The rename token 'Season.Number' has no value.")]
    [TestCase(17, null, 6, 7, null, "The rename token 'Episode.Number' has no value.")]
    [TestCase(17, 5, 6, null, null, "The rename token 'Episode.Number' has no value.")]
    public async Task PreviewImportRenameMaskAsync_RendersTheCoveredRange_WhenTheFileCoversSiblings(
        int? seasonNumber,
        int? firstNumber,
        int? secondNumber,
        int? thirdNumber,
        string? expectedFileName,
        string? expectedError)
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            Path = "Show",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        ContentHierarchyItem[] covered =
        [
            new()
            {
                ParentId = season.Id,
                ContentType = "tv",
                Level = "episode",
                Slug = "episode-2",
                Name = "Episode 2",
                Ordinal = 2
            },
            new()
            {
                ParentId = season.Id,
                ContentType = "tv",
                Level = "episode",
                Slug = "episode-3",
                Name = "Episode 3",
                Ordinal = 3
            }
        ];
        var path = Path.Combine(_rootFolder.Path, "Show", "Show.S01E01-E03.mkv");
        _rootFolder.RenameTemplate = "S{Season.Number:00}E{Episode.Number:00}.{Ext}";
        _leaf.ContentType = "tv";
        _leaf.Level = "episode";
        _leaf.Ordinal = 1;
        _leaf.ParentId = season.Id;
        season.MetadataContainer = new()
        {
            Contributions = { new() { ProviderType = "user", SequenceNumber = seasonNumber } }
        };
        _leaf.MetadataContainer = new()
        {
            Contributions = { new() { ProviderType = "user", SequenceNumber = firstNumber } }
        };
        covered[0].MetadataContainer = new()
        {
            Contributions = { new() { ProviderType = "user", SequenceNumber = secondNumber } }
        };
        covered[1].MetadataContainer = new()
        {
            Contributions = { new() { ProviderType = "user", SequenceNumber = thirdNumber } }
        };
        _files[path] = 5;
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(It.IsAny<string>())).Returns(false);
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "show",
                        DisplayName = "Show",
                        CustomMetadataFields = [],
                        Subfolder = string.Empty
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "season",
                        DisplayName = "Season",
                        CustomMetadataFields = []
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "episode",
                        DisplayName = "Episode",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = [".mkv"]
                    }
                ]
            });

        // Act
        var preview = await _subject.PreviewImportRenameMaskAsync([_leaf, season, show], covered, path)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                preview.Files.Single().DestinationPath,
                Is.EqualTo(expectedFileName is null ? null : Path.Combine(_rootFolder.Path, "Show", expectedFileName)));
            Assert.That(preview.Files.Single().Error, Is.EqualTo(expectedError));
        }
    }

    [TestCase(new string[0], "episode")]
    [TestCase(new[] { "22222222-2222-2222-2222-222222222222" }, "season")]
    public async Task PreviewRenameAsync_PlansThePathFromTheParent_WhenTheFileCoversSiblings(
        string[] covered,
        string expectedLastLevel)
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "Show", "Show.S01E01-E02.mkv"));
        var primary = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid("11111111-1111-1111-1111-111111111111"),
            FileKind = "Video",
            FilePath = original.FullName
        };
        foreach (var contentItemId in covered)
        {
            primary.Coverage.Add(new() { MediaFileId = primary.Id, ContentItemId = new Guid(contentItemId) });
        }

        _leaf.ContentType = "tv";
        _leaf.Level = "episode";
        _leaf.Ordinal = 1;
        _leaf.ParentId = season.Id;
        _leaf.ContentItem = new ContentItem { Id = primary.ContentItemId, MediaFiles = { primary } };
        var provider = new Mock<IPathResolutionContentTypeDefinitionProvider>();
        provider
            .Setup(p => p.BuildPathPlan(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(new ContentPathPlan
            {
                Directories = [new ContentPathPlanDirectory { LevelSlug = "show", Name = "Show" }],
                FileName = "Show.S01E01-E02.mkv"
            });
        _contentTypes.Setup(registry => registry.FindProviderFor("tv")).Returns(provider.Object);
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_leaf, season, show]);
        _contentHierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria =>
                    criteria.ParentId.And.Any(criterion => criterion.Value == season.Id)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    _leaf,
                    new()
                    {
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "episode-2",
                        Name = "Episode 2",
                        Ordinal = 2,
                        ContentItem = new() { Id = new Guid("22222222-2222-2222-2222-222222222222") }
                    }
                ],
                TotalItems = 2
            });
        _mediaFiles.Setup(repository => repository.GetByPathAsync(original.FullName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(primary);

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Item.Id, Is.EqualTo(_leaf.Id));
            provider.Verify(p => p.BuildPathPlan(
                It.Is<IReadOnlyList<ContentHierarchyItem>>(ancestors => ancestors.Last().Level == expectedLastLevel),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>(),
                "Show.S01E01-E02",
                "mkv"), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteRenameAsync_ReturnsUpdatedMediaFileAndWritesAudit_WhenPreviewIsCurrent()
    {
        // Arrange
        var originalPath = Path.Combine(_rootFolder.Path, "incoming", "raw.mkv");
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = originalPath,
            CreatedAt = DateTime.UtcNow
        };
        _mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.FilePaths!.Contains(originalPath)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [mediaFile] });
        _mediaFiles
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _files[originalPath] = 100;
        var preview = await _subject.PreviewRenameAsync(_leaf.Id, new FileInfo(originalPath)).ConfigureAwait(false);
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act
        var result = await _subject.ExecuteRenameAsync(preview, userId, correlationId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.FilePath, Is.EqualTo(preview.Rename.FullName));
            Assert.That(_files, Does.ContainKey(preview.Rename.FullName));
        }

        _mediaFiles.Verify(
            repository => repository.UpdateAsync(
                It.Is<MediaFile>(file =>
                    file.Id == mediaFile.Id && file.FilePath == preview.Rename.FullName),
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _auditRecords.Verify(
            repository => repository.CreateAsync(
                It.Is<AuditRecord>(audit =>
                    audit.Operation == "Rename" &&
                    audit.EntityId == mediaFile.Id.ToString() &&
                    audit.UserId == userId &&
                    audit.CorrelationId == correlationId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteRenameAsync_Throws_WhenPreviewBecomesStale()
    {
        // Arrange
        _rootFolder.RenameTemplate = "first/{OriginalFilename}.{Ext}";
        var originalPath = Path.Combine(_rootFolder.Path, "incoming", "raw.mkv");
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = originalPath,
            CreatedAt = DateTime.UtcNow
        };
        _mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.FilePaths!.Contains(originalPath)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [mediaFile] });
        _files[originalPath] = 100;
        var preview = await _subject.PreviewRenameAsync(_leaf.Id, new FileInfo(originalPath)).ConfigureAwait(false);
        _rootFolder.RenameTemplate = "second/{OriginalFilename}.{Ext}";

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.ExecuteRenameAsync(preview, null, null).GetAwaiter().GetResult(),
                Throws.InstanceOf<InvalidOperationException>());
            _fileSystem.Verify(f => f.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteRenameAsync_Throws_WhenCollisionAppears()
    {
        // Arrange
        var originalPath = Path.Combine(_rootFolder.Path, "incoming", "raw.mkv");
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = originalPath,
            CreatedAt = DateTime.UtcNow
        };
        _mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.FilePaths!.Contains(originalPath)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [mediaFile] });
        _files[originalPath] = 100;
        var preview = await _subject.PreviewRenameAsync(_leaf.Id, new FileInfo(originalPath)).ConfigureAwait(false);
        _files[preview.Rename.FullName] = 100;

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.ExecuteRenameAsync(preview, null, null).GetAwaiter().GetResult(),
                Throws.InstanceOf<InvalidOperationException>());
            _fileSystem.Verify(f => f.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteRenameAsync_Throws_WhenOriginalFileIsMissing()
    {
        // Arrange
        var originalPath = Path.Combine(_rootFolder.Path, "incoming", "raw.mkv");
        var preview = await _subject.PreviewRenameAsync(_leaf.Id, new FileInfo(originalPath)).ConfigureAwait(false);

        // Act
        // Assert
        Assert.That(
            () => _subject.ExecuteRenameAsync(preview, null, null).GetAwaiter().GetResult(),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task ExecuteRenameAsync_ReturnsExistingMediaFileWithoutMoving_WhenAlreadyAtTarget()
    {
        // Arrange
        var originalPath = Path.Combine(_rootFolder.Path, "The Matrix", "raw.mkv");
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = originalPath,
            CreatedAt = DateTime.UtcNow
        };
        _mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.FilePaths!.Contains(originalPath)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [mediaFile] });
        _files[originalPath] = 100;
        var preview = await _subject.PreviewRenameAsync(_leaf.Id, new FileInfo(originalPath)).ConfigureAwait(false);

        // Act
        var result = await _subject.ExecuteRenameAsync(preview, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(mediaFile.Id));
            Assert.That(result.FilePath, Is.EqualTo(originalPath));
        }

        _auditRecords.Verify(
            repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fileSystem.Verify(f => f.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task DeleteFilesAsync_DeletesRootAndDescendantFiles_WhenSubtreeHasFiles()
    {
        // Arrange
        var rootPath = Path.Combine(_rootFolder.Path, "root.mkv");
        var descendantPath = Path.Combine(_rootFolder.Path, "child.mkv");
        var descendantId = Guid.NewGuid();
        var rootContent = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            CreatedAt = DateTime.UtcNow
        };
        rootContent.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = rootContent.Id,
            FileKind = "Video",
            FilePath = rootPath
        });
        var descendant = _leaf with
        {
            Id = descendantId,
            ParentId = _leaf.Id,
            ContentItem = new ContentItem
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = descendantId,
                CreatedAt = DateTime.UtcNow
            }
        };
        descendant.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = descendant.ContentItem.Id,
            FileKind = "Video",
            FilePath = descendantPath
        });
        var root = _leaf with { ContentItem = rootContent };
        _files[rootPath] = 100;
        _files[descendantPath] = 100;

        // Act
        await _subject.DeleteFilesAsync([root, descendant]).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(rootPath), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(descendantPath), Times.Once);
        }
    }

    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public async Task DeleteFilesAsync_KeepsTheFileOnDisk_WhenAKeptItemStillHasTheFile(bool kept, int expectedDeletes)
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = Guid.NewGuid() });
        contentItem.MediaFiles.Add(mediaFile);
        _files[path] = 100;
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kept ? mediaFile : null);

        // Act
        await _subject.DeleteFilesAsync([_leaf with { ContentItem = contentItem }]).ConfigureAwait(false);

        // Assert
        _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(path), Times.Exactly(expectedDeletes));
    }

    [Test]
    public async Task DeleteFilesAsync_DeletesTheOtherFilesAndRecordsTheFailure_WhenOneFileCannotBeDeleted()
    {
        // Arrange
        var lockedPath = Path.Combine(_rootFolder.Path, "locked.mkv");
        var freePath = Path.Combine(_rootFolder.Path, "free.mkv");
        var content = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            CreatedAt = DateTime.UtcNow
        };
        content.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = content.Id,
            FileKind = "Video",
            FilePath = lockedPath
        });
        content.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = content.Id,
            FileKind = "Video",
            FilePath = freePath
        });
        _files[lockedPath] = 100;
        _files[freePath] = 100;
        _fileSystem.Setup(fileSystem => fileSystem.DeleteFile(lockedPath))
            .Throws(new IOException("The file is in use."));

        // Act
        await _subject.DeleteFilesAsync([_leaf with { ContentItem = content }]).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(freePath), Times.Once);
            _auditRecords.Verify(
                repository => repository.CreateAsync(
                    It.Is<AuditRecord>(record => record.Operation == "FileDeleteFailed"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ChangePathAsync_PatchesPath_WhenThePathIsAvailable()
    {
        // Arrange
        _leaf.Path = "old";
        _leaf.ModifiedAt = DateTime.UtcNow;
        _contentHierarchyItems
            .Setup(repository => repository.GetByIdAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_leaf);
        _contentHierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.AncestorId == _leaf.Id),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        _contentHierarchyItems
            .Setup(repository => repository.PatchAsync(
                It.IsAny<ContentHierarchyItemPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords
            .Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "PathChange" });
        var patch = new ContentHierarchyItemPatch
        {
            Path = new ScalarPatch<string?> { Value = "new" }
        };

        // Act
        var changed = await _subject.ChangePathAsync(_leaf.Id, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.True);
            _contentHierarchyItems.Verify(
                repository => repository.PatchAsync(
                    It.Is<ContentHierarchyItemPatch>(applied => applied.Path!.Value == "new"),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task AttachAsync_ReturnsPrimaryAndFulfills_WhenFileMatchesPrimaryAndSidecarContent()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var actingUserId = Guid.NewGuid();
        var lineageLocked = false;
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFiles = new List<MediaFile>();
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"],
                    AcceptedSidecarTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                It.IsAny<string>(),
                It.Is<IReadOnlyList<string>>(types => types.Contains("video/*")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileSystem.Setup(system => system.ResolveFileKind("video/x-matroska")).Returns(FileKind.Video);
        _contentHierarchyItems.Setup(repository => repository.LockAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .Callback(() => lineageLocked = true)
            .Returns(Task.CompletedTask);
        _mediaFiles.Setup(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaFile file, Guid? _, CancellationToken _) =>
            {
                Assert.That(lineageLocked, Is.True);
                mediaFiles.Add(file);
                return file;
            });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<MediaFile> { Items = mediaFiles, TotalItems = mediaFiles.Count });
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileAttached" });

        _contentItems.Setup(repository => repository.ReconcileMediaStateAsync(
                _leaf.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => contentItem.Fulfillment = FulfillmentStatus.Fulfilled)
            .Returns(Task.CompletedTask);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);

        // Act
        var result = await _subject.AttachAsync(_leaf.Id, path, actingUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.Not.Null);
            Assert.That(result.Result!.IsPrimary, Is.True);
            Assert.That(result.Result.MediaFile.FilePath, Is.EqualTo(path));
            Assert.That(contentItem.Fulfillment, Is.EqualTo(FulfillmentStatus.Fulfilled));
            Assert.That(mediaFiles, Has.Count.EqualTo(1));
        }
    }

    [TestCase("episode.mkv")]
    [TestCase("episode.srt")]
    public void AttachAsync_ThrowsConfigurationMessage_WhenFileTypesAreNotConfigured(string fileName)
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, fileName);
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.AttachAsync(_leaf.Id, path, Guid.NewGuid()).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo(
                "This content level has no configured file types."));
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenFileTypesAreNotConfigured()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "episode.mkv");
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = []
                }
            ]
        });

        // Act
        Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.AttachAsync(_leaf.Id, path, null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _contentItems.Verify(repository => repository.CreateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _contentItems.Verify(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task AttachImportedAsync_AttachesPrimary_WhenContainerHasNoFileTypes()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "episode.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFiles = new List<MediaFile>();
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = []
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.ResolveFileKind("video/x-matroska")).Returns(FileKind.Video);
        _mediaFiles.Setup(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaFile file, Guid? _, CancellationToken _) =>
            {
                mediaFiles.Add(file);
                return file;
            });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<MediaFile> { Items = mediaFiles, TotalItems = mediaFiles.Count });
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileAttached" });

        _contentItems.Setup(repository => repository.ReconcileMediaStateAsync(
                _leaf.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => contentItem.Fulfillment = FulfillmentStatus.Fulfilled)
            .Returns(Task.CompletedTask);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);

        // Act
        var result = await _subject.AttachImportedAsync(_leaf.Id, path, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.Not.Null);
            Assert.That(result.Result!.IsPrimary, Is.True);
            Assert.That(contentItem.Fulfillment, Is.EqualTo(FulfillmentStatus.Fulfilled));
        }
    }

    [Test]
    public async Task AttachImportedAsync_ReturnsExistingAssociation_WhenImportIsRetried()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.Is<IReadOnlyList<string>>(types => types.Contains("video/*")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);

        // Act
        var result = await _subject.AttachImportedAsync(_leaf.Id, path, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result?.MediaFile.Id, Is.EqualTo(mediaFile.Id));
            Assert.That(result.Result?.IsPrimary, Is.True);
        }

        _mediaFiles.Verify(repository => repository.CreateAsync(
            It.IsAny<MediaFile>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task AttachAsync_AddsCoverageAndReturnsTheStoredFile_WhenASiblingOwnsTheFile()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv");
        var parentId = Guid.NewGuid();
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = path
        };
        _leaf.ParentId = parentId;
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentHierarchyItems.Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ContentItemId == mediaFile.ContentItemId &&
                    criteria.ParentId.And.Any(criterion => criterion.Value == parentId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Guid.NewGuid()]);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaFile
            {
                Id = mediaFile.Id,
                ContentItemId = contentItem.Id,
                FileKind = "Video",
                FilePath = path
            });

        // Act
        var result = await _subject.AttachAsync(_leaf.Id, path, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result?.MediaFile.Id, Is.EqualTo(mediaFile.Id));
            Assert.That(result.Result?.MediaFile.ContentItemId, Is.EqualTo(contentItem.Id));
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.Is<MediaFile>(file => file.Id == mediaFile.Id &&
                    file.Coverage.Select(coverage => coverage.ContentItemId).SequenceEqual(new[] { contentItem.Id })),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachImportedAsync_ThrowsWithoutStoredChange_WhenASiblingOwnsTheFile()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv");
        var parentId = Guid.NewGuid();
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = path
        };
        _leaf.ParentId = parentId;
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentHierarchyItems.Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ContentItemId == mediaFile.ContentItemId &&
                    criteria.ParentId.And.Any(criterion => criterion.Value == parentId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Guid.NewGuid()]);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.AttachImportedAsync(_leaf.Id, path, null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("The selected file is already attached to content."));
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task AttachImportedAsync_ReturnsTheFileWithoutStoredChange_WhenTheFileCoversTheItem()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv");
        var parentId = Guid.NewGuid();
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = path
        };
        mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = contentItem.Id });
        _leaf.ParentId = parentId;
        _files[path] = 123;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentHierarchyItems.Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ContentItemId == mediaFile.ContentItemId &&
                    criteria.ParentId.And.Any(criterion => criterion.Value == parentId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Guid.NewGuid()]);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);

        // Act
        var result = await _subject.AttachImportedAsync(_leaf.Id, path, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result?.MediaFile.Id, Is.EqualTo(mediaFile.Id));
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenFileIsOutsideConfiguredRoot()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), "outside", "movie.mkv");
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.AttachAsync(_leaf.Id, path, null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("The selected file is outside the configured root folder."));
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task AttachAsync_ReturnsSidecarAndKeepsMissing_WhenFileIsAcceptedSidecar()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.srt");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFiles = new List<MediaFile>();
        _files[path] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("text/plain");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.Is<IReadOnlyList<string>>(types => types.Contains("video/*")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.Is<IReadOnlyList<string>>(types => types.Contains(".srt")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileSystem.Setup(system => system.ResolveFileKind("text/plain")).Returns(FileKind.Subtitle);
        _mediaFiles.Setup(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaFile file, Guid? _, CancellationToken _) =>
            {
                mediaFiles.Add(file);
                return file;
            });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<MediaFile> { Items = mediaFiles, TotalItems = mediaFiles.Count });
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileAttached" });

        // Act
        var result = await _subject.AttachAsync(_leaf.Id, path, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.Not.Null);
            Assert.That(result.Result!.IsPrimary, Is.False);
            Assert.That(contentItem.Fulfillment, Is.EqualTo(FulfillmentStatus.Missing));
            Assert.That(mediaFiles, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenFileTypeIsUnknown()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.unknown");
        _files[path] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        // Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.AttachAsync(_leaf.Id, path, null).ConfigureAwait(false));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("The selected file type is unknown."));
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenFileTypeIsUnsupported()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.txt");
        _files[path] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("text/plain");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.AttachAsync(_leaf.Id, path, null).GetAwaiter().GetResult(),
                Throws.InstanceOf<ArgumentException>());
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenFileIsMissing()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "missing.mkv");
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = []
                }
            ]
        });

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.AttachAsync(_leaf.Id, path, null).GetAwaiter().GetResult(),
                Throws.InstanceOf<ArgumentException>());
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenFileIsInaccessible()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        _files[path] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileSystem.Setup(system => system.OpenRead(path)).Throws<UnauthorizedAccessException>();

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.AttachAsync(_leaf.Id, path, null).GetAwaiter().GetResult(),
                Throws.InstanceOf<ArgumentException>());
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _transactionManager.Verify(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<AttachContentFileResult>>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenFileIsAlreadyAttached()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        _files[path] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaFile { ContentItemId = Guid.NewGuid(), FileKind = "Video", FilePath = path });

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.AttachAsync(_leaf.Id, path, null).GetAwaiter().GetResult(),
                Throws.InstanceOf<ArgumentException>());
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenWindowsPathDiffersOnlyByCase()
    {
        // Arrange
        var selectedPath = Path.Combine(_rootFolder.Path, "movie.mkv");
        _files[selectedPath] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(selectedPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                selectedPath,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByPathAsync(selectedPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaFile
            {
                ContentItemId = Guid.NewGuid(),
                FileKind = "Video",
                FilePath = selectedPath
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.AttachAsync(_leaf.Id, selectedPath, null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("The selected file is already attached to content."));
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _mediaFiles.Verify(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("The INSERT violated IX_content_mediaFile_PathKey.", typeof(ArgumentException))]
    [TestCase("The INSERT violated FK_content_mediaFile_content_contentItem_ContentItemId.", typeof(DbUpdateException))]
    public void AttachAsync_ThrowsExpectedException_WhenMediaFileInsertFails(
        string failureMessage,
        Type expectedExceptionType)
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _files[path] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _mediaFiles.Setup(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new InvalidOperationException(failureMessage)));
        _mediaFiles.Setup(repository => repository.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaFile?)null);

        // Act
        var exception = Assert.CatchAsync(
            async () => await _subject.AttachAsync(_leaf.Id, path, null).ConfigureAwait(false));

        // Assert
        Assert.That(exception, Is.TypeOf(expectedExceptionType));
    }

    [Test]
    public void AttachAsync_ThrowsDbUpdateException_WhenAuditInsertFails()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        _files[path] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileSystem.Setup(system => system.ResolveFileKind("video/x-matroska")).Returns(FileKind.Video);
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.FilePaths != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.ContentItemIds != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [mediaFile], TotalItems = 1 });
        _mediaFiles.Setup(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Audit insert failed."));

        // Act
        var exception = Assert.ThrowsAsync<DbUpdateException>(
            async () => await _subject.AttachAsync(_leaf.Id, path, null).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Audit insert failed."));
    }

    [Test]
    public async Task AttachAsync_ReturnsNullWithoutStoredChange_WhenContentIsUnknown()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act
        var result = await _subject.AttachAsync(unknownId, "unknown.mkv", null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Result, Is.Null);
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task UpdateFulfillmentAsync_ReturnsFalseWithoutStoredChange_WhenContentIsFulfilledExternally()
    {
        // Arrange
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.FulfilledExternally,
            CreatedAt = DateTime.UtcNow
        };
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });

        // Act
        var result = await _subject.UpdateFulfillmentAsync(_leaf.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            _mediaFiles.Verify(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()), Times.Never);
            _contentItems.Verify(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task UnlinkAsync_RemovesAssociationAndSetsMissing_WhenLastPrimaryIsRemoved()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var lineageLocked = false;
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        var mediaFiles = new List<MediaFile> { mediaFile };
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _contentHierarchyItems.Setup(repository => repository.LockAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .Callback(() => lineageLocked = true)
            .Returns(Task.CompletedTask);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Assert.That(lineageLocked, Is.True);
                mediaFiles.Remove(mediaFile);
                return true;
            });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<MediaFile> { Items = mediaFiles, TotalItems = mediaFiles.Count });
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileUnlinked" });

        _contentItems.Setup(repository => repository.ReconcileMediaStateAsync(
                _leaf.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => contentItem.Fulfillment = FulfillmentStatus.Missing)
            .Returns(Task.CompletedTask);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);

        // Act
        var result = await _subject.UnlinkAsync(_leaf.Id, mediaFile.Id, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Removed, Is.True);
            Assert.That(result.FulfillmentChanged, Is.True);
            Assert.That(contentItem.Fulfillment, Is.EqualTo(FulfillmentStatus.Missing));
            Assert.That(mediaFiles, Is.Empty);
        }

        _fileSystem.Verify(system => system.DeleteFile(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task UnlinkAsync_KeepsFulfilled_WhenSidecarIsRemoved()
    {
        // Arrange
        var primary = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = Path.Combine(_rootFolder.Path, "movie.mkv")
        };
        var sidecar = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = primary.ContentItemId,
            FileKind = "Text",
            FilePath = Path.Combine(_rootFolder.Path, "movie.srt")
        };
        var contentItem = new ContentItem
        {
            Id = primary.ContentItemId,
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFiles = new List<MediaFile> { primary, sidecar };
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                primary.FilePath,
                It.Is<IReadOnlyList<string>>(types => types.Contains("video/*")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByIdAsync(sidecar.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sidecar);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                sidecar.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                mediaFiles.Remove(sidecar);
                return true;
            });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<MediaFile> { Items = mediaFiles, TotalItems = mediaFiles.Count });
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileUnlinked" });

        // Act
        var result = await _subject.UnlinkAsync(_leaf.Id, sidecar.Id, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Removed, Is.True);
            Assert.That(result.FulfillmentChanged, Is.False);
            Assert.That(contentItem.Fulfillment, Is.EqualTo(FulfillmentStatus.Fulfilled));
            Assert.That(mediaFiles, Is.EqualTo([primary]));
        }

        _fileSystem.Verify(system => system.DeleteFile(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task UnlinkAsync_SetsMissing_WhenOnlySidecarRemains()
    {
        // Arrange
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var primary = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Audio",
            FilePath = Path.Combine(_rootFolder.Path, "track.flac")
        };
        var sidecar = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Audio",
            FilePath = Path.Combine(_rootFolder.Path, "playlist.m3u")
        };
        var mediaFiles = new List<MediaFile> { primary, sidecar };
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["audio/*"],
                    AcceptedSidecarTypes = [".m3u"]
                }
            ]
        });
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                sidecar.FilePath,
                It.Is<IReadOnlyList<string>>(types => types.Contains(".m3u")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                sidecar.FilePath,
                It.Is<IReadOnlyList<string>>(types => types.Contains("audio/*")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mediaFiles.Setup(repository => repository.GetByIdAsync(primary.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(primary);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                primary.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                mediaFiles.Remove(primary);
                return true;
            });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<MediaFile> { Items = mediaFiles, TotalItems = mediaFiles.Count });
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileUnlinked" });

        _contentItems.Setup(repository => repository.ReconcileMediaStateAsync(
                _leaf.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => contentItem.Fulfillment = FulfillmentStatus.Missing)
            .Returns(Task.CompletedTask);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);

        // Act
        var result = await _subject.UnlinkAsync(_leaf.Id, primary.Id, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Removed, Is.True);
            Assert.That(result.FulfillmentChanged, Is.True);
            Assert.That(contentItem.Fulfillment, Is.EqualTo(FulfillmentStatus.Missing));
            Assert.That(mediaFiles, Is.EqualTo([sidecar]));
        }
    }

    [TestCase(
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
        "33333333-3333-3333-3333-333333333333")]
    [TestCase(
        "22222222-2222-2222-2222-222222222222",
        "11111111-1111-1111-1111-111111111111",
        "33333333-3333-3333-3333-333333333333")]
    public async Task UnlinkAsync_KeepsTheFileOnTheOtherItems_WhenTheFileCoversSeveralItems(
        string owner,
        string firstCovered,
        string secondCovered)
    {
        // Arrange
        var contentItem = new ContentItem
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid(owner),
            FileKind = "Video",
            FilePath = Path.Combine(_rootFolder.Path, "S01E01-E03.mkv")
        };
        mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = new Guid(firstCovered) });
        mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = new Guid(secondCovered) });
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });

        // Act
        var result = await _subject.UnlinkAsync(_leaf.Id, mediaFile.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Removed, Is.True);
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.Is<MediaFile>(file =>
                    file.ContentItemId == new Guid("22222222-2222-2222-2222-222222222222") &&
                    file.Coverage.Select(coverage => coverage.ContentItemId)
                        .SequenceEqual(new[] { new Guid("33333333-3333-3333-3333-333333333333") })),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task UnlinkAsync_KeepsCoverageAddedAfterTheCheck_WhenTheFileChangesBeforeTheLock()
    {
        // Arrange
        var contentItem = new ContentItem
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var checkedFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid("22222222-2222-2222-2222-222222222222"),
            FileKind = "Video",
            FilePath = Path.Combine(_rootFolder.Path, "S01E01-E03.mkv")
        };
        checkedFile.Coverage.Add(new() { MediaFileId = checkedFile.Id, ContentItemId = contentItem.Id });
        var lockedFile = new MediaFile
        {
            Id = checkedFile.Id,
            ContentItemId = new Guid("22222222-2222-2222-2222-222222222222"),
            FileKind = "Video",
            FilePath = checkedFile.FilePath
        };
        lockedFile.Coverage.Add(new() { MediaFileId = checkedFile.Id, ContentItemId = contentItem.Id });
        lockedFile.Coverage.Add(new()
        {
            MediaFileId = checkedFile.Id,
            ContentItemId = new Guid("33333333-3333-3333-3333-333333333333")
        });
        _mediaFiles.SetupSequence(repository => repository.GetByIdAsync(checkedFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkedFile)
            .ReturnsAsync(lockedFile);
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });

        // Act
        var result = await _subject.UnlinkAsync(_leaf.Id, checkedFile.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Removed, Is.True);
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.Is<MediaFile>(file =>
                    file.ContentItemId == new Guid("22222222-2222-2222-2222-222222222222") &&
                    file.Coverage.Select(coverage => coverage.ContentItemId)
                        .SequenceEqual(new[] { new Guid("33333333-3333-3333-3333-333333333333") })),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsTrue_WhenOwnedRowIsRemovedConcurrently()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        _files[path] = 12;
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileDeleted" });

        _contentItems.Setup(repository => repository.ReconcileMediaStateAsync(
                _leaf.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => contentItem.Fulfillment = FulfillmentStatus.Missing)
            .Returns(Task.CompletedTask);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);

        // Act
        var result = await _subject.DeleteAsync(_leaf.Id, mediaFile.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Deleted, Is.True);
            Assert.That(result.FulfillmentChanged, Is.True);
            _fileSystem.Verify(system => system.DeleteFile(path), Times.Once);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record =>
                    record.Operation == "FileDeleted" &&
                    record.EntityId == mediaFile.Id.ToString() &&
                    record.ParentEntityId == _leaf.Id.ToString()),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task DeleteAsync_RecordsMissingFileDetail_WhenOnlyOwnedRowIsRemoved()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileDeleted" });

        // Act
        var result = await _subject.DeleteAsync(_leaf.Id, mediaFile.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Deleted, Is.True);
            _fileSystem.Verify(system => system.DeleteFile(It.IsAny<string>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record =>
                    record.Operation == "FileDeleted" &&
                    record.Detail == "Removed the missing file movie.mkv from content."),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesTheFileAndItsRowForEveryItem_WhenTheFileOfAnotherItemCoversTheItem()
    {
        // Arrange
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var owner = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = Guid.NewGuid(),
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = owner.Id,
            FileKind = "Video",
            FilePath = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv")
        };
        mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = contentItem.Id });
        _files[mediaFile.FilePath] = 100;
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentItems.Setup(repository => repository.GetByIdAsync(owner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });

        // Act
        var result = await _subject.DeleteAsync(_leaf.Id, mediaFile.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Deleted, Is.True);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(mediaFile.FilePath), Times.Once);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalseAndSkipsAudit_WhenFileAndOwnedRowAreAlreadyRemoved()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });

        // Act
        var result = await _subject.DeleteAsync(_leaf.Id, mediaFile.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Deleted, Is.False);
            Assert.That(result.FulfillmentChanged, Is.False);
            _fileSystem.Verify(system => system.DeleteFile(It.IsAny<string>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void DeleteAsync_KeepsTheHostFile_WhenTheRowTransactionFails()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        _files[path] = 12;
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The audit row failed."));

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.DeleteAsync(_leaf.Id, mediaFile.Id, null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("The audit row failed."));
            _fileSystem.Verify(system => system.DeleteFile(It.IsAny<string>()), Times.Never);
        }
    }

    [Test]
    public void DeleteAsync_RecordsTheAbandonedFile_WhenTheHostFileCannotBeDeleted()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "movie.mkv");
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = path
        };
        _files[path] = 12;
        _fileSystem.Setup(system => system.DeleteFile(path)).Throws(new IOException("The file is in use."));
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                mediaFile.Id,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        _contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        _contentItems.Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditRecord { Category = "Content", Operation = "FileDeleted" });

        // Act
        var exception = Assert.ThrowsAsync<MediaFileDeletionException>(
            async () => await _subject.DeleteAsync(_leaf.Id, mediaFile.Id, null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.InnerException, Is.TypeOf<IOException>());
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record =>
                    record.Operation == "FileDeleteFailed" &&
                    record.EntityId == mediaFile.Id.ToString() &&
                    record.ParentEntityId == _leaf.Id.ToString()),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void PreviewRenameAsync_Throws_WhenAStoredPathEscapesItsEffectiveParent()
    {
        // Arrange
        _leaf.Path = "../escape";
        _contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "movie",
                        DisplayName = "Movie",
                        CustomMetadataFields = [],
                        Subfolder = "movies"
                    }
                ]
            });
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", "source.mkv"));

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("Relative content path ../escape escapes its effective parent path."));
    }

    [Test]
    public async Task GetBrowserLocationAsync_ReturnsEffectiveContentPath_WhenDirectoryExists()
    {
        // Arrange
        _leaf.Path = "The Matrix";
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    Subfolder = "movies"
                }
            ]
        });

        // Act
        var result = await _subject.GetBrowserLocationAsync(_leaf.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RootPath, Is.EqualTo(_rootFolder.Path));
            Assert.That(result.InitialPath, Is.EqualTo(Path.Combine(_rootFolder.Path, "movies", "The Matrix")));
        }
    }

    [Test]
    public async Task GetEffectivePathAsync_ReturnsRootFolderCombinedWithComputedDirectory_WhenPathIsRelative()
    {
        // Arrange
        _leaf.Path = "The Matrix";
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    Subfolder = "movies"
                }
            ]
        });

        // Act
        var result = await _subject.GetEffectivePathAsync(_leaf.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(Path.Combine(_rootFolder.Path, "movies", "The Matrix")));
    }

    [Test]
    public async Task GetEffectivePathAsync_ReturnsStoredPath_WhenPathIsAbsolute()
    {
        // Arrange
        var absolutePath = Path.Combine(Path.GetTempPath(), "content-file-effective-path", "Avatar (2009)");
        _leaf.Path = absolutePath;

        // Act
        var result = await _subject.GetEffectivePathAsync(_leaf.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(absolutePath));
    }

    [Test]
    public async Task GetEffectivePathAsync_ReturnsStoredPath_WhenNoRootFolderIsAssigned()
    {
        // Arrange
        _leaf.Path = "Example Movie (2025)";
        _leaf.EffectiveRootFolderId = Guid.NewGuid();

        // Act
        var result = await _subject.GetEffectivePathAsync(_leaf.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo("Example Movie (2025)"));
    }

    [Test]
    public void BrowseAsync_ThrowsWithoutBrowsing_WhenPathIsOutsideConfiguredRoot()
    {
        // Arrange
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside", Guid.NewGuid().ToString("N"));

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.BrowseAsync(_leaf.Id, outsidePath).GetAwaiter().GetResult(),
                Throws.InstanceOf<ArgumentException>());
            _fileSystem.Verify(system => system.BrowseAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void BrowseAsync_ThrowsWithoutBrowsing_WhenPathContainsLink()
    {
        // Arrange
        var linkedPath = Path.Combine(_rootFolder.Path, "linked");
        _fileSystem.Setup(system => system.PathContainsLink(_rootFolder.Path, linkedPath)).Returns(true);

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.BrowseAsync(_leaf.Id, linkedPath).GetAwaiter().GetResult(),
                Throws.InstanceOf<ArgumentException>());
            _fileSystem.Verify(system => system.BrowseAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void AttachAsync_ThrowsWithoutStoredChange_WhenPathContainsLink()
    {
        // Arrange
        var linkedPath = Path.Combine(_rootFolder.Path, "linked", "movie.mkv");
        _files[linkedPath] = 12;
        _contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.PathContainsLink(_rootFolder.Path, linkedPath)).Returns(true);

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => _subject.AttachAsync(_leaf.Id, linkedPath, null).GetAwaiter().GetResult(),
                Throws.InstanceOf<ArgumentException>());
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(
        "22222222-2222-2222-2222-222222222222",
        new[] { "11111111-1111-1111-1111-111111111111", "33333333-3333-3333-3333-333333333333" },
        "44444444-4444-4444-4444-444444444444",
        "22222222-2222-2222-2222-222222222222",
        new[] { "33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444" })]
    [TestCase(
        "11111111-1111-1111-1111-111111111111",
        new[] { "22222222-2222-2222-2222-222222222222" },
        "44444444-4444-4444-4444-444444444444",
        "22222222-2222-2222-2222-222222222222",
        new[] { "44444444-4444-4444-4444-444444444444" })]
    [TestCase(
        "22222222-2222-2222-2222-222222222222",
        new[] { "11111111-1111-1111-1111-111111111111" },
        "22222222-2222-2222-2222-222222222222",
        "22222222-2222-2222-2222-222222222222",
        new string[0])]
    public async Task ReassignAsync_MovesOnlyTheSourceAssociation_WhenTheFileCoversSeveralItems(
        string owner,
        string[] covered,
        string targetItem,
        string expectedOwner,
        string[] expectedCovered)
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E03.mkv");
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var source = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { Id = new Guid("11111111-1111-1111-1111-111111111111") }
        };
        var target = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-4",
            Name = "Episode 4",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { Id = new Guid(targetItem) }
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid(owner),
            FileKind = "Video",
            FilePath = path
        };
        foreach (var contentItemId in covered)
        {
            mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = new Guid(contentItemId) });
        }

        _files[path] = 100;
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([source, season]);
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([target, season]);
        _contentTypes.Setup(registry => registry.GetDefinitionFor("tv")).Returns(new ContentTypeDefinition
        {
            Slug = "tv",
            DisplayName = "TV",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode",
                    DisplayName = "Episode",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"]
                }
            ]
        });
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                path,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentItems.Setup(repository => repository.QueryAsync(
                It.Is<ContentItemQuery>(query => query.ContentHierarchyItemId == target.Id),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [target.ContentItem], TotalItems = 1 });

        // Act
        var reassigned = await _subject.ReassignAsync(
            source.Id,
            mediaFile.Id,
            new() { TargetContentHierarchyItemId = target.Id, ExpectedFilePath = path },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reassigned, Is.True);
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.Is<MediaFile>(file => file.ContentItemId == new Guid(expectedOwner) &&
                    file.Coverage.Select(coverage => coverage.ContentItemId.ToString()).SequenceEqual(expectedCovered)),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void ReassignAsync_ThrowsWithoutStoredChange_WhenTheDestinationIsNotASiblingOfTheCoveredItems()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv");
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var firstSeason = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var secondSeason = firstSeason with { Id = Guid.NewGuid(), Slug = "season-2", Name = "Season 2" };
        var source = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = firstSeason.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { Id = Guid.NewGuid() }
        };
        var target = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = secondSeason.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-5",
            Name = "Episode 5",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = path
        };
        mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = source.ContentItem!.Id });
        _files[path] = 100;
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([source, firstSeason, show]);
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([target, secondSeason, show]);
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(async () => await _subject.ReassignAsync(
            source.Id,
            mediaFile.Id,
            new() { TargetContentHierarchyItemId = target.Id, ExpectedFilePath = path },
            null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                exception!.Message,
                Is.EqualTo("The destination is not a sibling of the other items that the file covers."));
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ReassignAsync_ReturnsTrueWithoutStoredChange_WhenOnlyTheTargetHasTheFile()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv");
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var source = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { Id = new Guid("11111111-1111-1111-1111-111111111111") }
        };
        var target = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { Id = new Guid("22222222-2222-2222-2222-222222222222") }
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid("22222222-2222-2222-2222-222222222222"),
            FileKind = "Video",
            FilePath = path
        };
        _files[path] = 100;
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([source, season]);
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([target, season]);

        // Act
        var reassigned = await _subject.ReassignAsync(
            source.Id,
            mediaFile.Id,
            new() { TargetContentHierarchyItemId = target.Id, ExpectedFilePath = path },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reassigned, Is.True);
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ReassignAsync_ReturnsTrueWithoutStoredChange_WhenTheTargetIsTheSource()
    {
        // Arrange
        var path = Path.Combine(_rootFolder.Path, "S01E01-E02.mkv");
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var source = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            EffectiveRootFolderId = _rootFolder.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { Id = new Guid("11111111-1111-1111-1111-111111111111") }
        };
        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid("11111111-1111-1111-1111-111111111111"),
            FileKind = "Video",
            FilePath = path
        };
        mediaFile.Coverage.Add(new()
        {
            MediaFileId = mediaFile.Id,
            ContentItemId = new Guid("22222222-2222-2222-2222-222222222222")
        });
        _files[path] = 100;
        _contentHierarchyItems
            .Setup(repository => repository.GetLineageAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([source, season]);
        _mediaFiles.Setup(repository => repository.GetByIdAsync(mediaFile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaFile);

        // Act
        var reassigned = await _subject.ReassignAsync(
            source.Id,
            mediaFile.Id,
            new() { TargetContentHierarchyItemId = source.Id, ExpectedFilePath = path },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reassigned, Is.True);
            _mediaFiles.Verify(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
