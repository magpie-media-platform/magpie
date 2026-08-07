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
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

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
    private Mock<IAuditRecordRepository> _auditRecords = null!;
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
        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository>();
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [_leaf]);
        _metadataContainers = new Mock<IMetadataContainerRepository>();
        _metadataContainers
            .Setup(r => r.GetRelationshipTitlesByContentHierarchyItemAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>());
        _rootFolders = new Mock<IRootFolderRepository>();
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
        _fileSystem
            .Setup(f => f.Move(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination, bool _) =>
            {
                var fileSize = _files[source];
                _files.Remove(source);
                _files[destination] = fileSize;
            });

        _mediaFiles = new Mock<IMediaFileRepository>();
        _auditRecords = new Mock<IAuditRecordRepository>();
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<MediaFile>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<MediaFile>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        _subject = new ContentFileService(
            _contentHierarchyItems.Object,
            _metadataContainers.Object,
            _rootFolders.Object,
            _contentTypes.Object,
            _fileSystem.Object,
            _mediaFiles.Object,
            _auditRecords.Object,
            transactionManager.Object,
            TimeProvider.System,
            NullLogger<ContentFileService>.Instance);
    }

    [TestCaseSource(typeof(ContentFileServiceTemplateCases), nameof(ContentFileServiceTemplateCases.Cases))]
    public async Task PreviewRenameAsync_ReturnsExpectedPath_WhenTokensAreRendered(
        string template,
        IReadOnlyDictionary<string, string?> tokens,
        string expected)
    {
        // Arrange
        _rootFolder.RenameTemplate = template;
        if (tokens.TryGetValue("Title", out var title))
        {
            _leaf.Name = title ?? string.Empty;
        }

        if (tokens.TryGetValue("Slug", out var slug) && slug is not null)
        {
            _leaf.Slug = slug;
        }

        var contribution = new MetadataContribution
        {
            ProviderType = "test",
            OriginalTitle = tokens.GetValueOrDefault("OriginalTitle"),
            SortTitle = tokens.GetValueOrDefault("SortTitle"),
            PublishDate = int.TryParse(tokens.GetValueOrDefault("Year"), out var year)
                ? new DateTime(year, 1, 1)
                : null,
            CreatedAt = DateTime.UtcNow
        };
        _leaf.MetadataContainer!.Contributions.Add(contribution);
        var extension = tokens.GetValueOrDefault("Ext");
        var fileName = string.IsNullOrEmpty(extension) ? "source" : "source." + extension;
        var original = new FileInfo(Path.Combine(_rootFolder.Path, "incoming", fileName));

        // Act
        var result = await _subject.PreviewRenameAsync(_leaf.Id, original).ConfigureAwait(false);

        // Assert
        var expectedPath = Path.Combine(
            _rootFolder.Path,
            expected.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        Assert.That(result.Rename.FullName, Is.EqualTo(expectedPath));
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
            .Setup(p => p.BuildPath(
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
                return $"{ancestors[0].Name}/{ancestors[^1].Name} " +
                    $"({year}) " +
                    $"{relationshipsByNode[ancestors[^1].Id]["network"][0]}/{originalFileName}.{extension}";
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
            .Setup(p => p.BuildPath(
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
        Assert.That(
            () => _subject.ExecuteRenameAsync(preview, null, null).GetAwaiter().GetResult(),
            Throws.InstanceOf<InvalidOperationException>());
        _fileSystem.Verify(f => f.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
        Assert.That(
            () => _subject.ExecuteRenameAsync(preview, null, null).GetAwaiter().GetResult(),
            Throws.InstanceOf<InvalidOperationException>());
        _fileSystem.Verify(f => f.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
}
