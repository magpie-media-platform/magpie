using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Metadata;
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

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public class PruneOrphanedSubjectsJobTests
{
    private static readonly JobContext Context = new(
        ExecutionId: Guid.NewGuid(),
        ScheduleTriggerId: null,
        EventTriggerId: null,
        ManualTriggerUserId: null,
        ExecutionMode: JobExecutionMode.Queue,
        Parameters: new Dictionary<string, string>(),
        Event: null);

    private Mock<ISubjectRepository> _subjectRepository = null!;
    private Mock<ISubjectService> _subjectService = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private Mock<IProgressReporter> _progress = null!;
    private PruneOrphanedSubjectsJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subjectRepository = new Mock<ISubjectRepository>();
        _subjectService = new Mock<ISubjectService>();
        _transactionManager = new Mock<ITransactionManager>();
        _progress = new Mock<IProgressReporter>();

        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>());
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<(bool StillPrunable, bool Deleted)>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                Func<CancellationToken, Task<(bool StillPrunable, bool Deleted)>> operation,
                CancellationToken cancellationToken) => operation(cancellationToken));

        _subject = new PruneOrphanedSubjectsJob(
            _subjectRepository.Object,
            _subjectService.Object,
            _transactionManager.Object,
            _progress.Object,
            NullLogger<PruneOrphanedSubjectsJob>.Instance);
    }

    [Test]
    public void Spec_HasExpectedShape_WhenRead()
    {
        // Arrange
        // Act
        var spec = PruneOrphanedSubjectsJob.Spec;
        var schedule = DefaultJobTriggerCatalog.ScheduleTriggers
            .Single(trigger => trigger.JobType == spec.JobType);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("PruneOrphanedSubjects"));
            Assert.That(spec.DisplayName, Is.EqualTo("Prune orphaned subjects"));
            Assert.That(spec.Category, Is.EqualTo("Metadata"));
            Assert.That(spec.Scope, Is.EqualTo(JobScope.Administrative));
            Assert.That(spec.OnDemand, Is.True);
            Assert.That(spec.Parameters, Is.Empty);
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerType));
            Assert.That(spec.SupportedTriggerModes, Is.EquivalentTo([JobTriggerMode.Manual, JobTriggerMode.Schedule]));
            Assert.That(spec.Timeout, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(spec.FailurePolicy, Is.EqualTo(JobFailurePolicy.Fail));
            Assert.That(schedule.Scope, Is.EqualTo(JobScope.Administrative));
            Assert.That(schedule.Schedule, Is.EqualTo("15 2 * * *"));
            Assert.That(schedule.Enabled, Is.True);
        }
    }

    [TestCase(true, "The subject cleanup completed. Deleted subjects: 1.")]
    [TestCase(false, "The subject cleanup completed. Deleted subjects: 0.")]
    public async Task ExecuteAsync_ReportsPrunedCount_WhenDeleteCompletes(
        bool deleted,
        string expectedMessage)
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        _subjectRepository
            .SetupSequence(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject> { Items = [new() { Id = subjectId, Kind = "person" }] })
            .ReturnsAsync(new PagedList<Subject> { Items = [new() { Id = subjectId, Kind = "person" }] });
        _subjectService
            .Setup(service => service.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleted);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectService.Verify(
                service => service.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(progress => progress.Report(1, expectedMessage), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsDelete_WhenCandidateNoLongerPrunable()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        _subjectRepository
            .SetupSequence(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject> { Items = [new() { Id = subjectId, Kind = "person" }] })
            .ReturnsAsync(new PagedList<Subject>());

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectService.Verify(
                service => service.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _progress.Verify(
                progress => progress.Report(1, "The subject cleanup completed. Deleted subjects: 0."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DeletesOnceAndReportsCounts_WhenRunTwice()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        var completionMessages = new List<string?>();
        _subjectRepository
            .SetupSequence(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject> { Items = [new() { Id = subjectId, Kind = "person" }] })
            .ReturnsAsync(new PagedList<Subject> { Items = [new() { Id = subjectId, Kind = "person" }] })
            .ReturnsAsync(new PagedList<Subject>());
        _subjectService
            .Setup(service => service.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _progress
            .Setup(progress => progress.Report(1, It.IsAny<string?>()))
            .Callback((double? _, string? message) => completionMessages.Add(message));

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectService.Verify(
                service => service.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(completionMessages, Is.EqualTo([
                "The subject cleanup completed. Deleted subjects: 1.",
                "The subject cleanup completed. Deleted subjects: 0."
            ]));
        }
    }
}
