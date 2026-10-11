using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class LibraryImportPathConcurrencyTests
{
    private ContentHierarchyItem _owner = null!;
    private ContentHierarchyItem _first = null!;
    private LibraryImportCandidate _firstCandidate = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchy = null!;
    private Mock<IContentFileService> _files = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IContentService> _content = null!;
    private ILibraryImportService _subject = null!;
    private Task _beforeTransaction = Task.CompletedTask;

    [SetUp]
    public void SetUp()
    {
        _beforeTransaction = Task.CompletedTask;
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Show",
            Slug = "show",
            Monitoring = MonitoringMode.Unmonitored
        };
        _owner = root with
        {
            Id = Guid.NewGuid(),
            ParentId = root.Id,
            Level = "season",
            Name = "Season",
            Slug = "season",
            Path = "Show",
            Settings = new() { ["content.path.origin"] = "Generated" }
        };
        _first = root with
        {
            Id = Guid.NewGuid(),
            ParentId = _owner.Id,
            Level = "episode",
            Name = "First",
            Slug = "first"
        };
        _firstCandidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/library/Show/first.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new()
                {
                    ProviderType = "test", ExternalIdKey = "episode", ExternalId = "1", Confidence = 1, Title = "First"
                }
            ]
        };
        var candidates = new ConcurrentDictionary<Guid, LibraryImportCandidate>
        {
            [_firstCandidate.Id] = _firstCandidate
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository>();
        candidateRepository.Setup(repository => repository.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => candidates.GetValueOrDefault(id));
        candidateRepository.Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid? _, CancellationToken _) => candidates.TryRemove(id, out _));
        _content = new Mock<IContentService>();
        _content.Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "test", "episode", "1", "tv", null, _firstCandidate.RootFolderId, false, null, null, null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((_first, (IReadOnlyCollection<Guid>)[]));
        _hierarchy = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        _hierarchy.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        _hierarchy.Setup(repository => repository.GetByIdAsync(_first.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_first);
        _hierarchy.Setup(repository => repository.GetByIdAsync(_owner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _owner);
        _hierarchy.Setup(repository => repository.GetLineageAsync(_first.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [_first, _owner, root]);
        _hierarchy.Setup(repository => repository.PatchAsync(
                It.IsAny<ContentHierarchyItemPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<ContentHierarchyItemPatch, Guid?, CancellationToken>((patch, _, _) =>
                _owner = _owner with
                {
                    Path = patch.Path!.Value,
                    Settings = patch.Settings!.Add!,
                    ModifiedAt = new DateTime(2026, 9, 21, 1, 0, 0, DateTimeKind.Utc)
                })
            .ReturnsAsync(true);
        _files = new Mock<IContentFileService>();
        _files.Setup(service => service.GetEffectivePathAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/generated/Show");
        _files.Setup(service => service.AttachImportedAsync(
                _first.Id, _firstCandidate.FilePath, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new() { MediaFile = new() { FileKind = "video", FilePath = "/library/Show/first.mkv" } }
            });
        _fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        _fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        _fileSystem.Setup(system => system.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => left.Replace('\\', '/') == right.Replace('\\', '/'));
        var transactions = new Mock<ITransactionManager>();
        transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<Guid>> operation, CancellationToken token) =>
            {
                await _beforeTransaction.ConfigureAwait(false);
                return await operation(token).ConfigureAwait(false);
            });
        var types = new Mock<IContentTypeRegistry>();
        types.Setup(registry => registry.GetChildLevels("tv", "episode")).Returns([]);
        _subject = new LibraryImportService(
            candidateRepository.Object,
            _content.Object,
            _files.Object,
            types.Object,
            _hierarchy.Object,
            _fileSystem.Object,
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);
    }

    [Test]
    public async Task ImportCandidateAsync_CompletesImport_WhenOnlyOtherOwnerFieldsChange()
    {
        // Arrange
        var discovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _beforeTransaction = discovered.Task;

        // Act
        var import = _subject.ImportCandidateAsync(
            _firstCandidate.Id, 0, LibraryImportMonitoringMode.Unmonitored, null, null);
        _owner = _owner with { Name = "Updated season", ModifiedAt = DateTime.UtcNow };
        discovered.SetResult();
        var result = await import.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(_owner.Name, Is.EqualTo("Updated season"));
            Assert.That(_owner.Monitoring, Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(_owner.Path!.Replace('\\', '/'), Is.EqualTo("/library/Show"));
        }
    }

    [TestCase("Other", "Generated")]
    [TestCase("Other", null)]
    [TestCase("Show", null)]
    [TestCase("/library/Show", null)]
    public async Task ImportCandidateAsync_RejectsImport_WhenThePathChangesOrBecomesUserOwned(
        string currentPath, string? currentOrigin)
    {
        // Arrange
        var discovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _beforeTransaction = discovered.Task;

        // Act
        var import = _subject.ImportCandidateAsync(
            _firstCandidate.Id, 0, LibraryImportMonitoringMode.Unmonitored, null, null);
        _owner = _owner with { Path = currentPath, Settings = new() };
        if (currentOrigin is not null)
        {
            _owner.Settings["content.path.origin"] = currentOrigin;
        }

        discovered.SetResult();
        var result = await import.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            _hierarchy.Verify(repository => repository.PatchAsync(
                It.IsAny<ContentHierarchyItemPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            _files.Verify(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
