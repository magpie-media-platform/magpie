using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Import;

[TestFixture]
public sealed class RootFolderContentImportItemJobTests
{
    private static readonly Guid CandidateId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid UserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid WaitedItemId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private Mock<ILibraryImportService> _importService = null!;
    private Mock<ILibraryImportCandidateRepository> _candidateRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IProgressReporter> _progressReporter = null!;
    private RootFolderContentImportItemJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _importService = new Mock<ILibraryImportService>();
        _candidateRepository = new Mock<ILibraryImportCandidateRepository>();
        _jobManager = new Mock<IJobManager>();
        _progressReporter = new Mock<IProgressReporter>();
        _subject = new RootFolderContentImportItemJob(
            _importService.Object,
            _candidateRepository.Object,
            _jobManager.Object,
            _progressReporter.Object,
            NullLogger<RootFolderContentImportItemJob>.Instance);
    }

    [Test]
    public async Task ExecuteAsync_QueuesOnlyTheRefreshOfTheItem_WhenImportIsWaiting()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Future,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Waiting,
                Message = "The import is waiting until the episode level below season 'Season 1' exists.",
                ContentHierarchyItemId = WaitedItemId
            });
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["monitoringMode"] = "Future"
            }, null, Guid.Parse("77777777-7777-7777-7777-777777777777"));

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _candidateRepository.Verify(repository => repository.TryUpdateTargetAsync(
                CandidateId, WaitedItemId, UserId, It.IsAny<CancellationToken>()), Times.Once);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "ContentMetadataRefreshItem" &&
                    !request.OnlyIfInactive &&
                    request.NotBeforeAt == null &&
                    request.Parameters!["contentHierarchyItemId"] == "88888888-8888-8888-8888-888888888888"),
                It.IsAny<CancellationToken>()), Times.Once);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.IsAny<JobEnqueueRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _progressReporter.Verify(reporter => reporter.Report(1,
                "The import is waiting until the episode level below season 'Season 1' exists."), Times.Once);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _candidateRepository.Verify(repository => repository.TryResetImportAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RecordsTheWaitedItemBeforeItQueuesTheRefresh_WhenImportIsWaiting()
    {
        // Arrange
        var candidateRepository = new Mock<ILibraryImportCandidateRepository>(MockBehavior.Strict);
        var jobManager = new Mock<IJobManager>(MockBehavior.Strict);
        var sequence = new MockSequence();
        _importService
            .InSequence(sequence)
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Inherit,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Waiting,
                Message = "The import is waiting until the episode level below season 'Season 1' exists.",
                ContentHierarchyItemId = WaitedItemId
            });
        candidateRepository
            .InSequence(sequence)
            .Setup(repository => repository.TryUpdateTargetAsync(
                CandidateId, WaitedItemId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        jobManager
            .InSequence(sequence)
            .Setup(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == "ContentMetadataRefreshItem"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);
        var subject = new RootFolderContentImportItemJob(
            _importService.Object,
            candidateRepository.Object,
            jobManager.Object,
            _progressReporter.Object,
            NullLogger<RootFolderContentImportItemJob>.Instance);
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") }, null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        jobManager.Verify(manager => manager.ExecuteAsync(
            It.Is<JobEnqueueRequest>(request => request.JobType == "ContentMetadataRefreshItem"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(JobExecutionStatus.Failed, "A metadata provider failed.",
        "The metadata refresh that the import waited for ended with status Failed: A metadata provider failed.")]
    [TestCase(JobExecutionStatus.Completed, "No metadata provider returned metadata, so the refresh made no changes.",
        "The metadata refresh that the import waited for ended with status Completed: No metadata provider returned " +
        "metadata, so the refresh made no changes.")]
    [TestCase(JobExecutionStatus.Cancelled, null,
        "The metadata refresh that the import waited for ended with status Cancelled.")]
    public void ExecuteAsync_FailsWithTheRefreshReasonAndQueuesNothing_WhenTheWaitedRefreshHasEnded(
        JobExecutionStatus refreshStatus,
        string? refreshMessage,
        string expectedReason)
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Inherit,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Waiting,
                Message = "The import is waiting until the episode level below season 'Season 1' exists.",
                ContentHierarchyItemId = WaitedItemId
            });
        _jobManager
            .Setup(manager => manager.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.JobType == "ContentMetadataRefreshItem" &&
                    query.Statuses!.Contains(refreshStatus) &&
                    query.ParameterValueReferences!.Contains("88888888-8888-8888-8888-888888888888")),
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
                        Status = refreshStatus,
                        StatusMessage = refreshMessage
                    }
                ]
            });
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["metadataRefreshEndedItemId"] = WaitedItemId.ToString("D")
            }, null);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(() => _subject.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo(expectedReason));
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                CandidateId, expectedReason, UserId, It.IsAny<CancellationToken>()), Times.Once);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.IsAny<JobEnqueueRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _candidateRepository.Verify(repository => repository.TryUpdateTargetAsync(
                It.IsAny<Guid>(), It.IsNotNull<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _candidateRepository.Verify(repository => repository.TryUpdateTargetAsync(
                CandidateId,
                null,
                UserId,
                CancellationToken.None),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_ClearsTheWaitedItemAfterTheReset_WhenTheResumedImportIsCancelled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var candidateRepository = new Mock<ILibraryImportCandidateRepository>(MockBehavior.Strict);
        var sequence = new MockSequence();
        _importService
            .InSequence(sequence)
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Inherit,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        candidateRepository
            .InSequence(sequence)
            .Setup(repository => repository.TryResetImportAsync(CandidateId, null, UserId, CancellationToken.None))
            .ReturnsAsync(true);
        candidateRepository
            .InSequence(sequence)
            .Setup(repository => repository.TryUpdateTargetAsync(CandidateId, null, UserId, CancellationToken.None))
            .ReturnsAsync(true);
        var subject = new RootFolderContentImportItemJob(
            _importService.Object,
            candidateRepository.Object,
            _jobManager.Object,
            _progressReporter.Object,
            NullLogger<RootFolderContentImportItemJob>.Instance);
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["metadataRefreshEndedItemId"] = WaitedItemId.ToString("D")
            }, null);

        // Act
        Assert.ThrowsAsync<OperationCanceledException>(() => subject.ExecuteAsync(context, cancellation.Token));

        // Assert
        candidateRepository.Verify(repository => repository.TryUpdateTargetAsync(
            CandidateId, null, UserId, CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ClearsTheWaitedItemAfterTheImport_WhenTheResumedImportIsSkipped()
    {
        // Arrange
        var candidateRepository = new Mock<ILibraryImportCandidateRepository>(MockBehavior.Strict);
        var sequence = new MockSequence();
        _importService
            .InSequence(sequence)
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Inherit,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Skipped,
                Message = "Library import candidate no longer exists; treating as already completed."
            });
        candidateRepository
            .InSequence(sequence)
            .Setup(repository => repository.TryUpdateTargetAsync(CandidateId, null, UserId, CancellationToken.None))
            .ReturnsAsync(true);
        var subject = new RootFolderContentImportItemJob(
            _importService.Object,
            candidateRepository.Object,
            _jobManager.Object,
            _progressReporter.Object,
            NullLogger<RootFolderContentImportItemJob>.Instance);
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["metadataRefreshEndedItemId"] = WaitedItemId.ToString("D")
            }, null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        candidateRepository.Verify(repository => repository.TryUpdateTargetAsync(
            CandidateId, null, UserId, CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WaitsOnTheNewItem_WhenTheEndedRefreshWasForAnotherItem()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Inherit,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Waiting,
                Message = "The import is waiting until the episode level below season 'Season 1' exists.",
                ContentHierarchyItemId = WaitedItemId
            });
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["metadataRefreshEndedItemId"] = "99999999-9999-9999-9999-999999999999"
            }, null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _candidateRepository.Verify(repository => repository.TryUpdateTargetAsync(
                CandidateId, WaitedItemId, UserId, It.IsAny<CancellationToken>()), Times.Once);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "ContentMetadataRefreshItem" &&
                    request.Parameters!["contentHierarchyItemId"] == "88888888-8888-8888-8888-888888888888"),
                It.IsAny<CancellationToken>()), Times.Once);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == "RootFolderContentImportResume"),
                It.IsAny<CancellationToken>()), Times.Never);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesInherit_WhenMonitoringModeMissing()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Inherit,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult { Status = LibraryImportCompletionStatus.Completed });
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") }, null, null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _importService.Verify(s => s.ImportCandidateAsync(
            CandidateId, 0, LibraryImportMonitoringMode.Inherit, UserId, It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PassesAcceptedPlan_WhenExplicitRenameIsRequested()
    {
        // Arrange
        var preview = new FileRenamePreview
        {
            HasMask = true,
            Files = [new() { CandidateId = CandidateId, OriginalPath = "/library/old.mkv",
                DestinationPath = "/library/new.mkv" }]
        };
        _importService.Setup(service => service.ImportWithRenameAsync(CandidateId, 0,
                LibraryImportMonitoringMode.Future, It.IsAny<FileRenamePreview>(), UserId,
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Completed,
                Message = "Association completed. Rename failed: collision."
            });
        var context = new JobContext(Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["monitoringMode"] = "Future",
                ["renamePreview"] = JsonSerializer.Serialize(preview)
            }, null, null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _importService.Verify(service => service.ImportWithRenameAsync(CandidateId, 0,
                LibraryImportMonitoringMode.Future, It.Is<FileRenamePreview>(plan =>
                    plan.Files.Count == 1 && plan.Files[0].OriginalPath == "/library/old.mkv" &&
                    plan.Files[0].DestinationPath == "/library/new.mkv"), UserId,
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
            _importService.Verify(service => service.ImportCandidateAsync(It.IsAny<Guid>(), It.IsAny<int>(),
                It.IsAny<LibraryImportMonitoringMode>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void ExecuteAsync_ResetsCandidate_WhenCancelled()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<LibraryImportMonitoringMode>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") }, null, null);

        // Act
        var exception = Assert.CatchAsync(() => _subject.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.TypeOf<OperationCanceledException>());
            _candidateRepository.Verify(repository => repository.TryResetImportAsync(
                CandidateId, null, UserId, CancellationToken.None), Times.Once);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            _candidateRepository.Verify(repository => repository.TryUpdateTargetAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void ExecuteAsync_LeavesTheCandidateImporting_WhenTheUpstreamIsThrottled()
    {
        // Arrange
        var retryAt = new DateTimeOffset(2026, 3, 1, 12, 30, 0, TimeSpan.Zero);
        _importService
            .Setup(s => s.ImportCandidateAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<LibraryImportMonitoringMode>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamThrottledException(retryAt));
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") }, null, null);

        // Act
        var exception = Assert.ThrowsAsync<UpstreamThrottledException>(() => _subject.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.RetryAt, Is.EqualTo(retryAt));
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            _candidateRepository.Verify(repository => repository.TryResetImportAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void ExecuteAsync_MarksCandidateFailed_WhenExecutionThrows()
    {
        // Arrange
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["monitoringMode"] = "invalid"
            }, null, null);

        // Act
        var exception = Assert.CatchAsync(() => _subject.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.TypeOf<ArgumentException>());
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                CandidateId,
                "A valid monitoring mode is required.",
                UserId,
                CancellationToken.None),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_RecordsExceptionChain_WhenExecutionThrowsWithInnerException()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<LibraryImportMonitoringMode>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outer failure", new Exception("inner failure")));
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") }, null, null);

        // Act
        var exception = Assert.CatchAsync(() => _subject.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.TypeOf<InvalidOperationException>());
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                CandidateId,
                "outer failure -> inner failure",
                UserId,
                CancellationToken.None),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ImportsSingleCandidate_WhenCandidateIdGiven()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Future,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult { Status = LibraryImportCompletionStatus.Completed });

        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: UserId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["monitoringMode"] = "Future"
            },
            Event: null,
            CorrelationId: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _importService.Verify(
            s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Future,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PassesMatchIndex_WhenProvided()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId,
                2,
                LibraryImportMonitoringMode.Monitored,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult { Status = LibraryImportCompletionStatus.Completed });

        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: UserId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["matchIndex"] = "2",
                ["monitoringMode"] = "Monitored"
            },
            Event: null,
            CorrelationId: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _importService.Verify(
            s => s.ImportCandidateAsync(
                CandidateId,
                2,
                LibraryImportMonitoringMode.Monitored,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenImportFails()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<LibraryImportMonitoringMode>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Failed,
                Message = "provider offline"
            });

        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: UserId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["monitoringMode"] = "Unmonitored"
            },
            Event: null,
            CorrelationId: null);

        // Act
        var exception = Assert.CatchAsync(() => _subject.ExecuteAsync(context));

        // Assert
        Assert.That(exception, Is.TypeOf<MagpieException>());
    }

    [Test]
    public async Task ExecuteAsync_DoesNotThrow_WhenCandidateSkipped()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<LibraryImportMonitoringMode>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult
            {
                Status = LibraryImportCompletionStatus.Skipped,
                Message = "already imported"
            });

        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: UserId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["monitoringMode"] = "Inherit"
            },
            Event: null,
            CorrelationId: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _importService.Verify(
            s => s.ImportCandidateAsync(
                CandidateId,
                0,
                LibraryImportMonitoringMode.Inherit,
                UserId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenCandidateIdMissing()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: UserId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null,
            CorrelationId: null);

        // Act
        var exception = Assert.CatchAsync(() => _subject.ExecuteAsync(context));

        // Assert
        Assert.That(exception, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task ExecuteAsync_AssociatesWithoutImportOrRename_WhenAssociationIsRequested()
    {
        // Arrange
        _importService
            .Setup(s => s.AssociateCandidateAsync(
                CandidateId, UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult { Status = LibraryImportCompletionStatus.Completed });
        var context = new JobContext(
            Guid.NewGuid(), null, null, UserId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["candidateId"] = CandidateId.ToString("D"),
                ["associate"] = "True",
                ["renamePreview"] = JsonSerializer.Serialize(new FileRenamePreview { HasMask = true, Files = [] })
            },
            null,
            null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _importService.Verify(
                s => s.AssociateCandidateAsync(CandidateId, UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _importService.Verify(
                s => s.ImportWithRenameAsync(
                    It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<LibraryImportMonitoringMode>(),
                    It.IsAny<FileRenamePreview>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _importService.Verify(
                s => s.ImportCandidateAsync(
                    It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<LibraryImportMonitoringMode>(), It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
