using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Import;

[TestFixture]
public sealed class RootFolderContentImportResumeJobTests
{
    private static readonly Guid ItemId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid WaitingCandidateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ImportingCandidateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrphanedCandidateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CancellingCandidateId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid UserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid CorrelationId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private Mock<ILibraryImportCandidateRepository> _candidateRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private RootFolderContentImportResumeJob _subject = null!;
    private JobContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        _candidateRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.Status == LibraryImportCandidateStatus.Importing &&
                    query.ContentHierarchyItemIds!.Contains(ItemId)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>
            {
                Items =
                [
                    new LibraryImportCandidate
                    {
                        Id = WaitingCandidateId,
                        RootFolderId = Guid.NewGuid(),
                        ScanRunId = Guid.NewGuid(),
                        FilePath = "/media/library/Show/WaitingCandidateId.mkv",
                        ContentType = "tv",
                        Status = LibraryImportCandidateStatus.Importing,
                        ContentHierarchyItemId = ItemId
                    },
                    new LibraryImportCandidate
                    {
                        Id = ImportingCandidateId,
                        RootFolderId = Guid.NewGuid(),
                        ScanRunId = Guid.NewGuid(),
                        FilePath = "/media/library/Show/ImportingCandidateId.mkv",
                        ContentType = "tv",
                        Status = LibraryImportCandidateStatus.Importing,
                        ContentHierarchyItemId = ItemId
                    },
                    new LibraryImportCandidate
                    {
                        Id = OrphanedCandidateId,
                        RootFolderId = Guid.NewGuid(),
                        ScanRunId = Guid.NewGuid(),
                        FilePath = "/media/library/Show/OrphanedCandidateId.mkv",
                        ContentType = "tv",
                        Status = LibraryImportCandidateStatus.Importing,
                        ContentHierarchyItemId = ItemId
                    },
                    new LibraryImportCandidate
                    {
                        Id = CancellingCandidateId,
                        RootFolderId = Guid.NewGuid(),
                        ScanRunId = Guid.NewGuid(),
                        FilePath = "/media/library/Show/CancellingCandidateId.mkv",
                        ContentType = "tv",
                        Status = LibraryImportCandidateStatus.Importing,
                        ContentHierarchyItemId = ItemId
                    }
                ]
            });
        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(manager => manager.QueryExecutionsAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        _jobManager
            .Setup(manager => manager.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(query => query.JobType == "RootFolderContentImportItem"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "RootFolderContentImportItem",
                        Status = JobExecutionStatus.Running,
                        Parameters = new() { ["candidateId"] = ImportingCandidateId.ToString("D") }
                    },
                    new JobExecution
                    {
                        JobType = "RootFolderContentImportItem",
                        Status = JobExecutionStatus.CancellationRequested,
                        Parameters = new() { ["candidateId"] = CancellingCandidateId.ToString("D") }
                    },
                    new JobExecution
                    {
                        JobType = "RootFolderContentImportItem",
                        Status = JobExecutionStatus.Completed,
                        CreatedByUserId = UserId,
                        CorrelationId = CorrelationId,
                        Parameters = new()
                        {
                            ["candidateId"] = WaitingCandidateId.ToString("D"),
                            ["matchIndex"] = "1",
                            ["monitoringMode"] = "Future",
                            ["associate"] = "True"
                        }
                    },
                    new JobExecution
                    {
                        JobType = "RootFolderContentImportItem",
                        Status = JobExecutionStatus.Failed,
                        Parameters = new() { ["candidateId"] = WaitingCandidateId.ToString("D"), ["matchIndex"] = "0" }
                    }
                ]
            });
        _subject = new RootFolderContentImportResumeJob(
            _candidateRepository.Object,
            _jobManager.Object,
            Mock.Of<IProgressReporter>(),
            NullLogger<RootFolderContentImportResumeJob>.Instance);
        _context = new JobContext(
            Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["contentHierarchyItemId"] = ItemId.ToString("D") }, null);
    }

    [Test]
    public async Task ExecuteAsync_StopsWithoutQueuingAnything_WhenARefreshOfTheItemIsQueued()
    {
        // Arrange
        var retryAt = new DateTime(2026, 3, 1, 12, 5, 0, DateTimeKind.Utc);
        _jobManager
            .Setup(manager => manager.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.JobType == "ContentMetadataRefreshItem" &&
                    query.Status == JobExecutionStatus.Queued &&
                    query.ParameterValueReferences!.Contains(ItemId.ToString("D"))),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ContentMetadataRefreshItem",
                        Status = JobExecutionStatus.Queued,
                        NotBeforeAt = retryAt
                    }
                ]
            });

        // Act
        await _subject.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.IsAny<JobEnqueueRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _candidateRepository.Verify(repository => repository.QueryAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_ResumesEachWaitingImportWithItsLatestParameters_WhenTheRefreshHasEnded()
    {
        // Act
        await _subject.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "RootFolderContentImportItem" &&
                    request.IsSuccessor &&
                    request.NotBeforeAt == null &&
                    request.UserId == UserId &&
                    request.CorrelationId == CorrelationId &&
                    request.Parameters!.Count == 5 &&
                    request.Parameters["candidateId"] == WaitingCandidateId.ToString("D") &&
                    request.Parameters["matchIndex"] == "1" &&
                    request.Parameters["monitoringMode"] == "Future" &&
                    request.Parameters["associate"] == "True" &&
                    request.Parameters["metadataRefreshEndedItemId"] == ItemId.ToString("D")),
                It.IsAny<CancellationToken>()), Times.Once);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == "RootFolderContentImportItem"),
                It.IsAny<CancellationToken>()), Times.Once);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                OrphanedCandidateId,
                "The import execution that waited for the metadata refresh no longer exists.",
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_FindsTheLatestImportOnALaterPage_WhenTheFirstPageHoldsOtherImports()
    {
        // Arrange
        _jobManager
            .Setup(manager => manager.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(query => query.JobType == "RootFolderContentImportItem"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 500),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    .. Enumerable.Range(0, 500).Select(_ => new JobExecution
                    {
                        JobType = "RootFolderContentImportItem",
                        Status = JobExecutionStatus.Completed,
                        Parameters = new() { ["candidateId"] = Guid.NewGuid().ToString("D") }
                    })
                ]
            });
        _jobManager
            .Setup(manager => manager.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(query => query.JobType == "RootFolderContentImportItem"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 500),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "RootFolderContentImportItem",
                        Status = JobExecutionStatus.Completed,
                        Parameters = new() { ["candidateId"] = WaitingCandidateId.ToString("D") }
                    }
                ]
            });

        // Act
        await _subject.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "RootFolderContentImportItem" &&
                    request.Parameters!["candidateId"] == WaitingCandidateId.ToString("D")),
                It.IsAny<CancellationToken>()), Times.Once);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                WaitingCandidateId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
