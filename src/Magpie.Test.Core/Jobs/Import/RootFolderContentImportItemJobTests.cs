using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Import;

[TestFixture]
public sealed class RootFolderContentImportItemJobTests
{
    private static readonly Guid CandidateId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid UserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

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
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<RootFolderContentImportItemJob>.Instance);
    }

    [Test]
    public async Task ExecuteAsync_QueuesDelayedSuccessorWithoutFailingCandidate_WhenImportIsWaiting()
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
                Message = "The import is waiting until the episode level below season 'Season 1' exists."
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
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "RootFolderContentImportItem" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.IsSuccessor &&
                    request.UserId == UserId &&
                    request.CorrelationId == Guid.Parse("77777777-7777-7777-7777-777777777777") &&
                    request.NotBeforeAt == new DateTime(2026, 3, 1, 12, 1, 0, DateTimeKind.Utc) &&
                    request.Parameters!.Count == 2 &&
                    request.Parameters["candidateId"] == "55555555-5555-5555-5555-555555555555" &&
                    request.Parameters["monitoringMode"] == "Future"),
                It.IsAny<CancellationToken>()), Times.Once);
            _progressReporter.Verify(reporter => reporter.Report(1,
                "The import is waiting until the episode level below season 'Season 1' exists. " +
                "The import will try again in 1 minute."), Times.Once);
            _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _candidateRepository.Verify(repository => repository.TryResetImportAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
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

        // Assert
        Assert.That(async () => await _subject.ExecuteAsync(context).ConfigureAwait(false),
            Throws.TypeOf<OperationCanceledException>());
        _candidateRepository.Verify(repository => repository.TryResetImportAsync(
            CandidateId, null, UserId, CancellationToken.None), Times.Once);
        _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
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

        // Assert
        Assert.That(async () => await _subject.ExecuteAsync(context).ConfigureAwait(false),
            Throws.TypeOf<ArgumentException>());
        _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
            CandidateId, "A valid monitoring mode is required.", UserId, CancellationToken.None), Times.Once);
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

        // Assert
        Assert.That(async () => await _subject.ExecuteAsync(context).ConfigureAwait(false),
            Throws.TypeOf<InvalidOperationException>());
        _candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
            CandidateId, "outer failure -> inner failure", UserId, CancellationToken.None), Times.Once);
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

        // Assert
        Assert.That(
            async () => await _subject.ExecuteAsync(context).ConfigureAwait(false),
            Throws.TypeOf<MagpieException>());
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

        // Assert
        Assert.That(
            async () => await _subject.ExecuteAsync(context).ConfigureAwait(false),
            Throws.TypeOf<ArgumentException>());
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
