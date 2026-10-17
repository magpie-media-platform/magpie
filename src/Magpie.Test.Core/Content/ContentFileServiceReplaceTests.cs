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
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Storage;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentFileServiceReplaceTests
{
    private const string Lineage = "0f0e0d0c0b0a49d8a1b2c3d4e5f60718";

    private static readonly MediaInfo OutputMediaInfo = new()
    {
        Container = "matroska,webm",
        DurationSeconds = 5.0,
        Tracks = [new MediaInfoTrack { Type = "Video", Name = "video:0", Codec = "hevc" }]
    };

    private Dictionary<string, long> _files = null!;
    private List<MediaFile> _records = null!;
    private List<string> _steps = null!;
    private List<AuditRecord> _audits = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IMediaFileRepository> _mediaFiles = null!;
    private string _folder = null!;
    private Guid _itemId;
    private MediaFile _source = null!;
    private IContentFileService _subject = null!;

    private static IEnumerable<TestCaseData> DifferentNameCrashStates()
    {
        yield return new TestCaseData((object)new[] { $"The Matrix.mkv.{Lineage}.partial", "The Matrix.avi" })
            .SetArgDisplayNames("record committed");
        yield return new TestCaseData((object)new[] { "The Matrix.mkv", "The Matrix.avi" })
            .SetArgDisplayNames("output moved");
        yield return new TestCaseData((object)new[] { "The Matrix.mkv" })
            .SetArgDisplayNames("old file deleted");
    }

    private static IEnumerable<TestCaseData> SameNameCrashStates()
    {
        yield return new TestCaseData(
            (object)new[] { $"The Matrix.mkv.{Lineage}.partial", $"The Matrix.mkv.magpie-replace-{Lineage}.orig" })
            .SetArgDisplayNames("old file moved to the backup");
        yield return new TestCaseData(
            (object)new[] { "The Matrix.mkv", $"The Matrix.mkv.magpie-replace-{Lineage}.orig" })
            .SetArgDisplayNames("output moved");
    }

    [SetUp]
    public void SetUp()
    {
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Movies",
            Path = Path.Combine(Path.GetTempPath(), "content-file-replace", Guid.NewGuid().ToString("N")),
            ContentType = "movie",
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Slug = "the-matrix",
            Name = "The Matrix",
            EffectiveRootFolderId = rootFolder.Id,
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            MetadataContainer = new MetadataContainer
            {
                Id = Guid.NewGuid(),
                CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = leaf.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _itemId = leaf.Id;
        _folder = Path.Combine(rootFolder.Path, "The Matrix");
        _source = new()
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            MimeType = "video/x-msvideo",
            FilePath = Path.Combine(_folder, "The Matrix.avi"),
            FileSize = 900,
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _files = new(StringComparer.OrdinalIgnoreCase) { [_source.FilePath] = 900 };
        _records = [_source];
        _steps = [];
        _audits = [];

        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy.Setup(repository => repository.GetByIdAsync(leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaf);
        hierarchy.Setup(repository => repository.GetLineageAsync(leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([leaf]);
        var metadataContainers = new Mock<IMetadataContainerRepository>();
        metadataContainers
            .Setup(repository => repository.GetRelationshipTitlesByContentHierarchyItemAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>());
        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders.Setup(repository => repository.GetByIdAsync(rootFolder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootFolder);
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
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

        _fileSystem = new();
        _fileSystem.Setup(system => system.FileExists(It.IsAny<string>()))
            .Returns((string path) => _files.ContainsKey(path));
        _fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>()))
            .Returns((string path) => Path.GetFullPath(path));
        _fileSystem.Setup(system => system.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => Path.GetRelativePath(left, right) == ".");
        _fileSystem.Setup(system => system.IsPathWithin(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string root, string path) =>
                !Path.GetRelativePath(root, path).StartsWith("..", StringComparison.Ordinal));
        _fileSystem.Setup(system => system.LookupMimeTypeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem.Setup(system => system.MatchesAnyFileTypeAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileSystem.Setup(system => system.ResolveFileKind("video/x-matroska")).Returns(FileKind.Video);
        _fileSystem.Setup(system => system.ReadFileState(It.IsAny<string>()))
            .Returns((string path) => new FileState
            {
                Size = _files[path],
                LastWriteTimeUtc = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc)
            });
        _fileSystem.Setup(system => system.GetFileVersion(It.IsAny<string>()))
            .Returns((string path) => _files.TryGetValue(path, out var size) ? $"{size}" : null);
        _fileSystem.Setup(system => system.AcquireImportPaths(It.IsAny<IReadOnlyList<string>>()))
            .Returns(Mock.Of<IDisposable>());
        _fileSystem.Setup(system => system.Move(It.IsAny<string>(), It.IsAny<string>(), false))
            .Callback((string source, string destination, bool _) =>
            {
                _steps.Add($"move {Path.GetFileName(source)} to {Path.GetFileName(destination)}");
                _files[destination] = _files[source];
                _files.Remove(source);
            });
        _fileSystem.Setup(system => system.DeleteFile(It.IsAny<string>()))
            .Callback((string path) =>
            {
                _steps.Add($"delete {Path.GetFileName(path)}");
                _files.Remove(path);
            });

        _mediaFiles = new();
        _mediaFiles.Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _records.FirstOrDefault(record => record.Id == id));
        _mediaFiles.Setup(repository => repository.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
                _records.FirstOrDefault(record =>
                    string.Equals(record.FilePath, path, StringComparison.OrdinalIgnoreCase)));
        _mediaFiles.Setup(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaFile record, Guid? _, CancellationToken _) =>
            {
                _steps.Add($"create record {Path.GetFileName(record.FilePath)}");
                record.Id = Guid.NewGuid();
                _records.Add(record);
                return record;
            });
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaFile record, Guid? _, CancellationToken _) =>
            {
                _steps.Add($"update record {Path.GetFileName(record.FilePath)}");
                _records[_records.FindIndex(stored => stored.Id == record.Id)] = record;
                return true;
            });
        _mediaFiles.Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid? _, CancellationToken _) =>
            {
                _steps.Add($"delete record {Path.GetFileName(_records.Single(record => record.Id == id).FilePath)}");
                return _records.RemoveAll(record => record.Id == id) > 0;
            });
        var contentItems = new Mock<IContentRepository>();
        contentItems.Setup(repository => repository.GetByIdAsync(contentItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        contentItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [contentItem], TotalItems = 1 });
        var auditRecords = new Mock<IAuditRecordRepository>();
        auditRecords.Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditRecord audit, CancellationToken _) =>
            {
                _audits.Add(audit);
                return audit;
            });
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
            hierarchy.Object,
            metadataContainers.Object,
            rootFolders.Object,
            contentTypes.Object,
            new SubjectKindRegistry([], NullLogger<SubjectKindRegistry>.Instance),
            _fileSystem.Object,
            _mediaFiles.Object,
            contentItems.Object,
            auditRecords.Object,
            transactionManager.Object,
            TimeProvider.System,
            NullLogger<ContentFileService>.Instance);
    }

    [Test]
    public async Task ReplaceAsync_CommitsTheRecordBeforeTheFileAndDeletesTheOldFileLast_WhenTheNameChanges()
    {
        // Arrange
        _files[Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600;

        // Act
        var placed = await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _steps,
                Is.EqualTo(
                [
                    "create record The Matrix.mkv",
                    $"move The Matrix.mkv.{Lineage}.partial to The Matrix.mkv",
                    "delete The Matrix.avi",
                    "delete record The Matrix.avi"
                ]));
            Assert.That(placed!.FilePath, Is.EqualTo(Path.Combine(_folder, "The Matrix.mkv")));
            Assert.That(placed.FileSize, Is.EqualTo(600));
            Assert.That(placed.MediaInfo, Is.SameAs(OutputMediaInfo));
            Assert.That(_files.Keys, Is.EqualTo([Path.Combine(_folder, "The Matrix.mkv")]));
        }
    }

    [Test]
    public async Task ReplaceAsync_UpdatesTheRecordInPlaceAndDeletesTheBackupLast_WhenTheNameStays()
    {
        // Arrange
        _source.FilePath = Path.Combine(_folder, "The Matrix.mkv");
        _files = new(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_folder, "The Matrix.mkv")] = 900,
            [Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600
        };

        // Act
        var placed = await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _steps,
                Is.EqualTo(
                [
                    $"move The Matrix.mkv to The Matrix.mkv.magpie-replace-{Lineage}.orig",
                    $"move The Matrix.mkv.{Lineage}.partial to The Matrix.mkv",
                    "update record The Matrix.mkv",
                    $"delete The Matrix.mkv.magpie-replace-{Lineage}.orig"
                ]));
            Assert.That(placed!.Id, Is.EqualTo(_source.Id));
            Assert.That(placed.FileSize, Is.EqualTo(600));
            Assert.That(placed.MediaInfo, Is.SameAs(OutputMediaInfo));
            Assert.That(placed.ReplacedAt, Is.Not.Null);
            Assert.That(
                _files,
                Is.EqualTo(new Dictionary<string, long> { [Path.Combine(_folder, "The Matrix.mkv")] = 600 }));
            Assert.That(
                _audits.Select(audit => (audit.Operation, audit.EntityId, audit.Detail)),
                Is.EqualTo([("Replace", _source.Id.ToString(), "Replaced The Matrix.mkv.")]));
        }
    }

    [Test]
    public void ReplaceAsync_FailsWithoutAChange_WhenAFileExistsAtTheRealPath()
    {
        // Arrange
        _files[Path.Combine(_folder, "The Matrix.mkv")] = 700;
        _files[Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600;

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("Rename target file already exists"));
            Assert.That(_steps, Is.Empty);
            Assert.That(_records, Is.EqualTo([_source]));
        }
    }

    [Test]
    public void ReplaceAsync_RemovesTheNewRecordAndKeepsTheSource_WhenTheOutputCannotMoveToTheRealPath()
    {
        // Arrange
        _files[Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600;
        _fileSystem
            .Setup(system => system.Move(
                Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
                Path.Combine(_folder, "The Matrix.mkv"),
                false))
            .Throws(new IOException("The disk is full."));

        // Act
        Assert.ThrowsAsync<IOException>(async () => await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_steps, Is.EqualTo(["create record The Matrix.mkv", "delete record The Matrix.mkv"]));
            Assert.That(_records, Is.EqualTo([_source]));
            Assert.That(_files.Keys, Is.EquivalentTo(
            [
                Path.Combine(_folder, "The Matrix.avi"),
                Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")
            ]));
        }
    }

    [Test]
    public void ReplaceAsync_RestoresTheOldFile_WhenTheOutputCannotMoveIntoPlace()
    {
        // Arrange
        _source.FilePath = Path.Combine(_folder, "The Matrix.mkv");
        _files = new(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_folder, "The Matrix.mkv")] = 900,
            [Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600
        };
        _fileSystem
            .Setup(system => system.Move(
                Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
                Path.Combine(_folder, "The Matrix.mkv"),
                false))
            .Throws(new IOException("The disk is full."));

        // Act
        Assert.ThrowsAsync<IOException>(async () => await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_files, Is.EquivalentTo(new Dictionary<string, long>
            {
                [Path.Combine(_folder, "The Matrix.mkv")] = 900,
                [Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600
            }));
            Assert.That(_records.Single().FileSize, Is.EqualTo(900));
        }
    }

    [Test]
    public void ReplaceAsync_RestoresTheOldFileAndTheOutput_WhenTheRecordUpdateFails()
    {
        // Arrange
        _source.FilePath = Path.Combine(_folder, "The Matrix.mkv");
        _files = new(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_folder, "The Matrix.mkv")] = 900,
            [Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600
        };
        _mediaFiles.Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _steps,
                Is.EqualTo(
                [
                    $"move The Matrix.mkv to The Matrix.mkv.magpie-replace-{Lineage}.orig",
                    $"move The Matrix.mkv.{Lineage}.partial to The Matrix.mkv",
                    $"move The Matrix.mkv to The Matrix.mkv.{Lineage}.partial",
                    $"move The Matrix.mkv.magpie-replace-{Lineage}.orig to The Matrix.mkv"
                ]));
            Assert.That(_files, Is.EquivalentTo(new Dictionary<string, long>
            {
                [Path.Combine(_folder, "The Matrix.mkv")] = 900,
                [Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600
            }));
        }
    }

    [Test]
    public async Task ReplaceAsync_KeepsTheOldRecordAndRecordsTheFailure_WhenTheOldFileCannotBeDeleted()
    {
        // Arrange
        _files[Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600;
        _fileSystem.Setup(system => system.DeleteFile(_source.FilePath)).Throws(new IOException("The file is in use."));

        // Act
        var placed = await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(placed!.FilePath, Is.EqualTo(Path.Combine(_folder, "The Matrix.mkv")));
            Assert.That(_records, Has.Member(_source));
            Assert.That(
                _audits.Select(audit => (audit.Operation, audit.EntityId)),
                Has.Member(("FileDeleteFailed", _source.Id.ToString())));
        }
    }

    [Test]
    public async Task ReplaceAsync_KeepsTheOldFileAndRecord_WhenTheOldFileChangesDuringThePlacement()
    {
        // Arrange
        _files[Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600;
        _fileSystem.SetupSequence(system => system.GetFileVersion(_source.FilePath)).Returns("900").Returns("901");

        // Act
        await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _steps,
                Is.EqualTo(
                [
                    "create record The Matrix.mkv",
                    $"move The Matrix.mkv.{Lineage}.partial to The Matrix.mkv"
                ]));
            Assert.That(_files, Does.ContainKey(_source.FilePath));
            Assert.That(_records, Has.Member(_source));
            Assert.That(
                _audits.Select(audit => (audit.Operation, audit.EntityId)),
                Has.Member(("FileDeleteFailed", _source.Id.ToString())));
        }
    }

    [Test]
    public async Task ReplaceAsync_WritesTheOutputStateIntoTheRecord_WhenARecordOfTheItemAlreadyHasTheRealPath()
    {
        // Arrange
        _files[Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600;
        _records.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _source.ContentItemId,
            FileKind = "Video",
            FilePath = Path.Combine(_folder, "The Matrix.mkv"),
            FileSize = 300,
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Act
        var placed = await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _steps,
                Is.EqualTo(
                [
                    "update record The Matrix.mkv",
                    $"move The Matrix.mkv.{Lineage}.partial to The Matrix.mkv",
                    "delete The Matrix.avi",
                    "delete record The Matrix.avi"
                ]));
            Assert.That(placed!.FileSize, Is.EqualTo(600));
            Assert.That(placed.MediaInfo, Is.SameAs(OutputMediaInfo));
        }
    }

    [Test]
    public void ReplaceAsync_RestoresTheOldFile_WhenTheSameNamePlacementLostItsRecordBetweenTheMoves()
    {
        // Arrange
        _source.FilePath = Path.Combine(_folder, "The Matrix.mkv");
        _records.Clear();
        _files = new(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600,
            [Path.Combine(_folder, $"The Matrix.mkv.magpie-replace-{Lineage}.orig")] = 900
        };

        // Act
        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Is.EqualTo("The media file record is gone. The old file was restored."));
            Assert.That(
                _files,
                Is.EqualTo(new Dictionary<string, long>
                {
                    [Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial")] = 600,
                    [Path.Combine(_folder, "The Matrix.mkv")] = 900
                }));
            Assert.That(_records, Is.Empty);
        }
    }

    [TestCaseSource(nameof(DifferentNameCrashStates))]
    public async Task ReplaceAsync_FinishesThePlacement_WhenItRepeatsAfterACrashWithANewName(string[] files)
    {
        // Arrange
        _files = files.ToDictionary(name => Path.Combine(_folder, name), _ => 600L, StringComparer.OrdinalIgnoreCase);
        _records.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _source.ContentItemId,
            FileKind = "Video",
            FilePath = Path.Combine(_folder, "The Matrix.mkv"),
            FileSize = 600,
            MediaInfo = OutputMediaInfo,
            CreatedAt = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc)
        });

        // Act
        var placed = await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(placed!.FilePath, Is.EqualTo(Path.Combine(_folder, "The Matrix.mkv")));
            Assert.That(_files.Keys, Is.EqualTo([Path.Combine(_folder, "The Matrix.mkv")]));
            Assert.That(
                _records.Select(record => record.FilePath),
                Is.EqualTo([Path.Combine(_folder, "The Matrix.mkv")]));
        }
    }

    [TestCaseSource(nameof(SameNameCrashStates))]
    public async Task ReplaceAsync_FinishesThePlacement_WhenItRepeatsAfterACrashWithTheSameName(string[] files)
    {
        // Arrange
        _source.FilePath = Path.Combine(_folder, "The Matrix.mkv");
        _files = files.ToDictionary(name => Path.Combine(_folder, name), _ => 600L, StringComparer.OrdinalIgnoreCase);

        // Act
        var placed = await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(placed!.Id, Is.EqualTo(_source.Id));
            Assert.That(placed.MediaInfo, Is.SameAs(OutputMediaInfo));
            Assert.That(_files.Keys, Is.EqualTo([Path.Combine(_folder, "The Matrix.mkv")]));
        }
    }

    [Test]
    public void ReplaceAsync_ThrowsWithoutAChange_WhenNoOutputIsStagedOrPlaced()
    {
        // Act
        Assert.ThrowsAsync<FileNotFoundException>(async () => await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, $"The Matrix.mkv.{Lineage}.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_steps, Is.Empty);
            Assert.That(_records, Is.EqualTo([_source]));
        }
    }

    [Test]
    public void ReplaceAsync_ThrowsWithoutAChange_WhenTheStagedFileNameDoesNotNameTheOutput()
    {
        // Arrange
        _files[Path.Combine(_folder, "other.partial")] = 600;

        // Act
        Assert.ThrowsAsync<ArgumentException>(async () => await _subject.ReplaceAsync(
            _itemId,
            _source.Id,
            Path.Combine(_folder, "other.partial"),
            "The Matrix.mkv",
            OutputMediaInfo,
            null,
            _ => Task.CompletedTask).ConfigureAwait(false));

        // Assert
        Assert.That(_steps, Is.Empty);
    }
}
