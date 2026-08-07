using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Import;
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

    private Mock<ILibraryImportService> _importService = null!;
    private RootFolderContentImportItemJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _importService = new Mock<ILibraryImportService>();
        _subject = new RootFolderContentImportItemJob(
            _importService.Object,
            Mock.Of<IProgressReporter>(),
            NullLogger<RootFolderContentImportItemJob>.Instance);
    }

    [Test]
    public async Task ExecuteAsync_ImportsSingleCandidate_WhenCandidateIdGiven()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId, 0, UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportCompletionResult { Status = LibraryImportCompletionStatus.Completed });

        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: UserId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") },
            Event: null,
            CorrelationId: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _importService.Verify(
            s => s.ImportCandidateAsync(
                CandidateId, 0, UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PassesMatchIndex_WhenProvided()
    {
        // Arrange
        _importService
            .Setup(s => s.ImportCandidateAsync(
                CandidateId, 2, UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
                ["matchIndex"] = "2"
            },
            Event: null,
            CorrelationId: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _importService.Verify(
            s => s.ImportCandidateAsync(
                CandidateId, 2, UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
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
            Parameters: new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") },
            Event: null,
            CorrelationId: null);

        // Act / Assert
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
            Parameters: new Dictionary<string, string> { ["candidateId"] = CandidateId.ToString("D") },
            Event: null,
            CorrelationId: null);

        // Act / Assert
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

        // Act / Assert
        Assert.That(
            async () => await _subject.ExecuteAsync(context).ConfigureAwait(false),
            Throws.TypeOf<ArgumentException>());
    }
}
