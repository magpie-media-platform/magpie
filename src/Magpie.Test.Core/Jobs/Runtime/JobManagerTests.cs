using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Jobs.Runtime.Scheduling;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public class JobManagerTests
{
    private Mock<IJobRuntime> _runtime = null!;
    private Mock<IJobExecutionRepository> _executionRepository = null!;
    private Mock<IScheduleTriggerRepository> _scheduleTriggerRepository = null!;
    private Mock<IEventTriggerRepository> _eventTriggerRepository = null!;
    private Mock<IJobExecutionScopeFactory> _jobExecutionScopes = null!;
    private FakeTimeProvider _timeProvider = null!;
    private IJobManager _subject = null!;

    public static IEnumerable<TestCaseData> ParameterValidationCases
    {
        get
        {
            yield return new TestCaseData(DataType.Number, "0x10", "Parameter value must be a valid number.");
            yield return new TestCaseData(DataType.Boolean, "1", "Parameter value must be a valid boolean.");
            yield return new TestCaseData(DataType.Date, "2000", "Parameter value must be a valid date.");
        }
    }

    [SetUp]
    public void SetUp()
    {
        _runtime = new Mock<IJobRuntime>();
        _executionRepository = new Mock<IJobExecutionRepository>();
        _scheduleTriggerRepository = new Mock<IScheduleTriggerRepository>();
        _eventTriggerRepository = new Mock<IEventTriggerRepository>();
        _jobExecutionScopes = new Mock<IJobExecutionScopeFactory>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

        _subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _jobExecutionScopes.Object,
            _timeProvider,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());
    }

    [Test]
    public async Task ExecuteAsync_CreatesExecution_AndReturnsId()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = executionId,
            JobType = "TestJob",
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        var result = await _subject.ExecuteAsync(
            new JobEnqueueRequest { JobType = "TestJob", Mode = JobExecutionMode.Queue }).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(executionId));
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e =>
                    e.JobType == "TestJob" &&
                    e.ExecutionMode == JobExecutionMode.Queue &&
                    e.Status == JobExecutionStatus.Queued),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StampsParentExecutionId_WhenSpawnedFromRunningJob()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ChildJob",
            Status = JobExecutionStatus.Queued
        };
        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);
        _jobExecutionScopes.SetupGet(value => value.CurrentExecutionId).Returns(parentId);

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "ChildJob",
                Mode = JobExecutionMode.Queue,
            },
            default).ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.ParentExecutionId == parentId),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_LeavesParentNull_ForTopLevelTrigger()
    {
        // Arrange
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "TopLevelJob",
            Status = JobExecutionStatus.Queued
        };
        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(new JobEnqueueRequest { JobType = "TopLevelJob", Mode = JobExecutionMode.Queue })
            .ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.ParentExecutionId == null),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CallsRuntimeExecute_WhenModeIsImmediate()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = executionId,
            JobType = "TestJob",
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest { JobType = "TestJob", Mode = JobExecutionMode.Immediate }).ConfigureAwait(false);

        // Assert
        _runtime.Verify(m => m.ExecuteAsync(executionId, It.IsAny<CancellationToken>()), Times.Once);
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.ExecutionMode == JobExecutionMode.Immediate),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AcquiresImmediateSlot_WhenModeIsImmediate()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = executionId,
            JobType = "TestJob",
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        var options = new JobRuntimeOptions();
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            TimeProvider.System,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            scheduler,
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        await subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "TestJob",
                Mode = JobExecutionMode.Immediate
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Total, Is.EqualTo(options.MaxConcurrentJobs));
            _runtime.Verify(m => m.ExecuteAsync(executionId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotCallRuntimeExecute_WhenImmediateCoalescesOntoRunningExecution()
    {
        // Arrange
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Status = JobExecutionStatus.Running
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        var result = await _subject.ExecuteAsync(
            new JobEnqueueRequest { JobType = "TestJob", Mode = JobExecutionMode.Immediate }).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(execution.Id));
        _runtime.Verify(m => m.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotCallRuntimeExecute_WhenModeIsQueue()
    {
        // Arrange
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(new JobEnqueueRequest { JobType = "TestJob", Mode = JobExecutionMode.Queue })
            .ConfigureAwait(false);

        // Assert
        _runtime.Verify(m => m.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_PassesParameters_WhenExecutingJob()
    {
        // Arrange
        var parameters = new Dictionary<string, string> { ["key1"] = "value1", ["key2"] = "value2" };
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "TestJob",
                Parameters = parameters,
                Mode = JobExecutionMode.Queue,
            }).ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e =>
                    e.Parameters.ContainsKey("key1") &&
                    e.Parameters["key1"] == "value1" &&
                    e.Parameters.ContainsKey("key2") &&
                    e.Parameters["key2"] == "value2"),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SetsScope_WhenSpecIsRegistered()
    {
        // Arrange
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Content,
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        await subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "TestJob",
                Mode = JobExecutionMode.Queue
            },
            default).ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.Scope == JobScope.Content),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DefaultsScopeToSystem_WhenSpecMissing()
    {
        // Arrange
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "UnknownJob",
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(new JobEnqueueRequest { JobType = "UnknownJob", Mode = JobExecutionMode.Queue })
            .ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.Scope == JobScope.System),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SetsExtensionId_WhenExecutingExtensionJob()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            ExtensionId = extensionId,
            Status = JobExecutionStatus.Queued
        };

        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "TestJob",
                ExtensionId = extensionId,
                Mode = JobExecutionMode.Queue,
            }).ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.ExtensionId == extensionId),
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task QueryExecutionsAsync_DelegatesToRepository_WhenExecutionsAreRequested()
    {
        // Arrange
        var query = new JobExecutionQuery();
        var paging = new PagingParameter { PageNumber = 1, PageSize = 10 };
        var executions = new PagedList<JobExecution>
        {
            PageNumber = 1,
            PageSize = 10,
            TotalItems = 0,
        };

        _executionRepository
            .Setup(m => m.QueryAsync(query, paging, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(executions);

        // Act
        var result = await _subject.QueryExecutionsAsync(query, paging, null).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.SameAs(executions));
    }

    [Test]
    public async Task GetExecutionAsync_DelegatesToRepository_WhenExecutionIsRequested()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = executionId,
            JobType = "TestJob",
            Status = JobExecutionStatus.Running
        };

        _executionRepository
            .Setup(m => m.GetByIdAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        var result = await _subject.GetExecutionAsync(executionId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.SameAs(execution));
    }

    [Test]
    public async Task GetExecutionAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        _executionRepository
            .Setup(m => m.GetByIdAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(JobExecution?));

        // Act
        var result = await _subject.GetExecutionAsync(executionId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [TestCase(1, "Pending")]
    [TestCase(0, "Queued")]
    [TestCase(-1, "Queued")]
    public async Task GetExecutionTreeAsync_ReturnsStatus_WhenNotBeforeAtIsRelativeToNow(
        int secondsFromNow,
        string expectedStatus)
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = executionId,
            JobType = "TestJob",
            Status = JobExecutionStatus.Queued,
            NotBeforeAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(secondsFromNow)
        };
        _executionRepository
            .Setup(repository => repository.GetLineageAsync(executionId, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync([execution]);

        // Act
        var result = await _subject.GetExecutionTreeAsync(executionId, includeSiblings: false).ConfigureAwait(false);

        // Assert
        Assert.That(result!.Root!.Status, Is.EqualTo(expectedStatus));
    }

    [Test]
    public async Task RequestCancellationAsync_DelegatesToRuntime_WhenCancellationIsRequested()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        _runtime
            .Setup(m => m.TryCancelAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.RequestCancellationAsync(executionId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _runtime.Verify(m => m.TryCancelAsync(executionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RequestCancellationAsync_ReturnsFalse_WhenRuntimeReturnsFalse()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        _runtime
            .Setup(m => m.TryCancelAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _subject.RequestCancellationAsync(executionId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_DelegatesToRepository_AndComputesNextRun()
    {
        // Arrange
        var create = new ScheduleTriggerCreate
        {
            ExtensionId = null,
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true
        };

        var userId = Guid.NewGuid();

        var createdTrigger = new ScheduleTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestTrigger",
            Schedule = "0 * * * *"
        };

        _scheduleTriggerRepository
            .Setup(m => m.CreateAsync(It.IsAny<ScheduleTrigger>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTrigger);

        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    SupportedTriggerModes = new[] { JobTriggerMode.Schedule },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var result = await subject.CreateScheduleTriggerAsync(create, userId, Role.Admin, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestTrigger"));
        _scheduleTriggerRepository.Verify(
            r => r.CreateAsync(
                It.Is<ScheduleTrigger>(t =>
                    t.Name == "TestTrigger" &&
                    t.Schedule == "0 * * * *" &&
                    t.NextRunAt.HasValue),
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void CreateScheduleTriggerAsync_Throws_WhenCallerRoleInsufficient()
    {
        // Arrange
        var create = new ScheduleTriggerCreate
        {
            ExtensionId = null,
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true
        };

        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    SupportedTriggerModes = new[] { JobTriggerMode.Schedule },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Assert
        Assert.ThrowsAsync<MagpieException>(
            async () => await subject.CreateScheduleTriggerAsync(
                create,
                Guid.NewGuid(),
                Role.Manager,
                default)
                .ConfigureAwait(false));
        _scheduleTriggerRepository.Verify(
            r => r.CreateAsync(
                It.IsAny<ScheduleTrigger>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void CreateScheduleTriggerAsync_ThrowsArgumentException_WhenScheduleIsInvalid()
    {
        // Arrange
        var create = new ScheduleTriggerCreate
        {
            ExtensionId = null,
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestTrigger",
            Schedule = "99 99 * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true,
        };

        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    SupportedTriggerModes = new[] { JobTriggerMode.Schedule },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await subject.CreateScheduleTriggerAsync(
                create,
                Guid.NewGuid(),
                Role.Admin,
                default)
                .ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.ParamName, Is.EqualTo("create"));
        }

        _scheduleTriggerRepository.Verify(
            r => r.CreateAsync(
                It.IsAny<ScheduleTrigger>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task PatchScheduleTriggerAsync_RecomputesNextRunAt_WhenScheduleChanges()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trigger = new ScheduleTrigger
        {
            Id = triggerId,
            JobType = "TestJob",
            Scope = JobScope.User,
            Name = "TestTrigger",
            Schedule = "*/5 * * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true
        };
        var patch = new ScheduleTriggerPatch
        {
            Id = triggerId,
            Schedule = new() { Value = "*/5 * * * *" }
        };

        _scheduleTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trigger);
        _scheduleTriggerRepository
            .Setup(m => m.PatchAsync(It.IsAny<ScheduleTriggerPatch>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _scheduleTriggerRepository
            .Setup(m => m.UpdateAsync(It.IsAny<ScheduleTrigger>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.PatchScheduleTriggerAsync(triggerId, patch, userId, callerIsAdmin: true)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _scheduleTriggerRepository.Verify(
            r => r.UpdateAsync(
                It.Is<ScheduleTrigger>(t => t.Id == triggerId && t.NextRunAt.HasValue),
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void PatchScheduleTriggerAsync_ThrowsArgumentException_WhenScheduleIsInvalid()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var trigger = new ScheduleTrigger
        {
            Id = triggerId,
            JobType = "TestJob",
            Scope = JobScope.User,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true
        };
        var patch = new ScheduleTriggerPatch
        {
            Id = triggerId,
            Schedule = new() { Value = "99 99 * * *" },
        };

        _scheduleTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trigger);

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _subject.PatchScheduleTriggerAsync(
                triggerId,
                patch,
                Guid.NewGuid(),
                callerIsAdmin: true)
                .ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.ParamName, Is.EqualTo("patch"));
        }

        _scheduleTriggerRepository.Verify(
            r => r.PatchAsync(
                It.IsAny<ScheduleTriggerPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task PatchScheduleTriggerAsync_ClearsNextRunAt_WhenTriggerIsDisabled()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trigger = new ScheduleTrigger
        {
            Id = triggerId,
            JobType = "TestJob",
            Scope = JobScope.User,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = false,
            NextRunAt = DateTime.UtcNow.AddHours(1)
        };
        var patch = new ScheduleTriggerPatch
        {
            Id = triggerId,
            Enabled = new() { Value = false }
        };

        _scheduleTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trigger);
        _scheduleTriggerRepository
            .Setup(m => m.PatchAsync(It.IsAny<ScheduleTriggerPatch>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _scheduleTriggerRepository
            .Setup(m => m.UpdateAsync(It.IsAny<ScheduleTrigger>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.PatchScheduleTriggerAsync(triggerId, patch, userId, callerIsAdmin: true)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _scheduleTriggerRepository.Verify(
            r => r.UpdateAsync(
                It.Is<ScheduleTrigger>(t => t.Id == triggerId && !t.NextRunAt.HasValue),
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateEventTriggerAsync_DelegatesToRepository_WhenTriggerIsCreated()
    {
        // Arrange
        var eventFilter = new EventFilter
        {
            EventName = new()
            {
                Value = "test.event"
            }
        };
        var create = new EventTriggerCreate
        {
            ExtensionId = null,
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = eventFilter,
            Enabled = true
        };

        var userId = Guid.NewGuid();

        var createdTrigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = eventFilter
        };

        _eventTriggerRepository
            .Setup(m => m.CreateAsync(It.IsAny<EventTrigger>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTrigger);

        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    SupportedTriggerModes = new[] { JobTriggerMode.Event },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var result = await subject.CreateEventTriggerAsync(create, userId, Role.Admin, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestEventTrigger"));
    }

    [Test]
    public void CreateEventTriggerAsync_Throws_WhenEventBindingIsMalformed()
    {
        // Arrange
        var create = new EventTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = new EventFilter(),
            Parameters = new Dictionary<string, string> { ["itemId"] = "${event.content.itemId" },
        };
        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    SupportedTriggerModes = new[] { JobTriggerMode.Event },
                    Parameters = new[] { new JobParameter("itemId", null, DataType.Number, true, null) },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.CreateEventTriggerAsync(create, Guid.NewGuid(), Role.Admin, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Event binding is malformed."));
        _eventTriggerRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<EventTrigger>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCaseSource(nameof(ParameterValidationCases))]
    public void CreateEventTriggerAsync_Throws_WhenEventParameterTypeIsInvalid(
        DataType type,
        string value,
        string expected)
    {
        // Arrange
        var create = new EventTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = new EventFilter(),
            Parameters = new Dictionary<string, string> { ["value"] = value },
        };
        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    SupportedTriggerModes = new[] { JobTriggerMode.Event },
                    Parameters = new[] { new JobParameter("value", null, type, true, null) },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.CreateEventTriggerAsync(create, Guid.NewGuid(), Role.Admin, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(expected));
        _eventTriggerRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<EventTrigger>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void EnqueueAsync_Throws_WhenRequiredParameterIsMissing()
    {
        // Arrange
        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "TestJob",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    OnDemand = true,
                    SupportedTriggerModes = new[] { JobTriggerMode.Manual },
                    Parameters = new[] { new JobParameter("value", null, DataType.String, true, null) },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.EnqueueAsync(
                new JobEnqueueRequest { JobType = "TestJob", Parameters = new Dictionary<string, string>() },
                Guid.NewGuid(),
                Role.Admin,
                default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Required parameter value is missing."));
        _executionRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void PatchEventTriggerAsync_Throws_WhenEventParameterIsInvalid()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _eventTriggerRepository
            .Setup(repository => repository.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = triggerId,
                JobType = "TestJob",
                Scope = JobScope.Administrative,
                Name = "Test event trigger",
                EventFilter = new EventFilter(),
                Parameters = new Dictionary<string, string> { ["itemId"] = "1" },
            });
        var patch = new EventTriggerPatch
        {
            Parameters = new()
            {
                Update = new Dictionary<string, string> { ["itemId"] = "not-a-number" },
            },
        };
        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "TestJob",
                    DisplayName = "Test job",
                    Category = "Test",
                    Scope = JobScope.Administrative,
                    SupportedTriggerModes = new[] { JobTriggerMode.Event },
                    Parameters = new[] { new JobParameter("itemId", null, DataType.Number, true, null) },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.PatchEventTriggerAsync(triggerId, patch, userId, callerIsAdmin: true, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Parameter itemId must be a valid number."));
        _eventTriggerRepository.Verify(
            repository => repository.PatchAsync(
                It.IsAny<EventTriggerPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void GetSpecs_ReturnsRegisteredSpecs_WhenSpecsAreRequested()
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "TestJob",
            DisplayName = "TestJob",
            Category = "Test",
            Scope = JobScope.Administrative,
        };

        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var specs = subject.GetSpecs(null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(specs, Has.Count.EqualTo(1));
            Assert.That(specs[0], Is.SameAs(spec));
        }
    }

    [Test]
    public void GetSpecs_KeepsLastRegistration_WhenJobTypeRegisteredTwice()
    {
        // Arrange
        var first = new JobSpec
        {
            JobType = "TestJob",
            DisplayName = "First",
            Category = "Test",
            Scope = JobScope.Administrative,
        };
        var second = new JobSpec
        {
            JobType = "TestJob",
            DisplayName = "Second",
            Category = "Test",
            Scope = JobScope.Administrative,
        };

        var subject = new JobManager(
            _runtime.Object,
            new JobRegistry(
                [
                    new JobRegistration(first, typeof(WishlistRefreshItemJob)),
                    new JobRegistration(second, typeof(WishlistRefreshItemJob)),
                ],
                NullLogger<JobRegistry>.Instance),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var specs = subject.GetSpecs(null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(specs, Has.Count.EqualTo(1));
            Assert.That(specs[0], Is.SameAs(second));
        }
    }

    [Test]
    public void GetSpecs_ReturnsOnlyOnDemandManualSpecs_WhenManualMode()
    {
        // Arrange
        var onDemand = new JobSpec
        {
            JobType = "OnDemandJob",
            DisplayName = "OnDemandJob",
            Category = "Test",
            Scope = JobScope.Administrative,
            OnDemand = true,
            SupportedTriggerModes = [JobTriggerMode.Manual],
        };
        var scheduledOnly = new JobSpec
        {
            JobType = "ScheduledJob",
            DisplayName = "ScheduledJob",
            Category = "Test",
            Scope = JobScope.Administrative,
            OnDemand = false,
            SupportedTriggerModes = [JobTriggerMode.Schedule],
        };

        var subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { onDemand, scheduledOnly }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>());

        // Act
        var specs = subject.GetSpecs(JobTriggerMode.Manual);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(specs, Has.Count.EqualTo(1));
            Assert.That(specs[0], Is.SameAs(onDemand));
        }
    }

    [TestCase(ConcurrencyScope.Parallel, null)]
    [TestCase(ConcurrencyScope.PerType, "type:magpie.job")]
    [TestCase(ConcurrencyScope.Global, "global")]
    [TestCase(ConcurrencyScope.PerTypeAndTarget, "type:magpie.job|contentItemId=abc-123")]
    [TestCase(ConcurrencyScope.PerTarget, "contentItemId=abc-123")]
    public void ResolveLockPartition_Result_WhenScope(ConcurrencyScope scope, string? expected)
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "magpie.job",
            DisplayName = "Magpie job",
            Category = "Test",
            Scope = JobScope.System,
            ConcurrencyScope = scope,
            LockPartitionFields = ["contentItemId"]
        };
        IJobManager subject = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            Mock.Of<IJobExecutionRepository>(),
            Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLogger<JobManager>.Instance);
        var parameters = new Dictionary<string, string?> { ["contentItemId"] = "abc-123" };

        // Act
        var partition = subject.ResolveLockPartition("magpie.job", parameters);

        // Assert
        Assert.That(partition, Is.EqualTo(expected));
    }

    [TestCase(ConcurrencyScope.PerTarget, true)]
    [TestCase(ConcurrencyScope.PerTypeAndTarget, false)]
    public void ResolveLockPartition_SameResult_WhenTypesDiffer(ConcurrencyScope scope, bool expectedSame)
    {
        // Arrange
        var search = new JobSpec
        {
            JobType = "magpie.releaseSearch",
            DisplayName = "Release search",
            Category = "Test",
            Scope = JobScope.System,
            ConcurrencyScope = scope,
            LockPartitionFields = ["contentItemId"]
        };
        var refresh = new JobSpec
        {
            JobType = "magpie.refreshMetadata",
            DisplayName = "Refresh metadata",
            Category = "Test",
            Scope = JobScope.System,
            ConcurrencyScope = scope,
            LockPartitionFields = ["contentItemId"]
        };
        IJobManager subject = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { search, refresh }),
            Mock.Of<IJobExecutionRepository>(),
            Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLogger<JobManager>.Instance);
        var parameters = new Dictionary<string, string?> { ["contentItemId"] = "abc-123" };

        // Act
        var searchPartition = subject.ResolveLockPartition(search.JobType, parameters);
        var refreshPartition = subject.ResolveLockPartition(refresh.JobType, parameters);

        // Assert
        Assert.That(searchPartition == refreshPartition, Is.EqualTo(expectedSame));
    }

    [Test]
    public void ResolveLockPartition_SameResult_WhenFieldsDeclaredInDifferentOrder()
    {
        // Arrange
        var ordered = new JobSpec
        {
            JobType = "ordered",
            DisplayName = "Ordered",
            Category = "Test",
            Scope = JobScope.System,
            ConcurrencyScope = ConcurrencyScope.PerTarget,
            LockPartitionFields = ["a", "b"]
        };
        var reversed = new JobSpec
        {
            JobType = "reversed",
            DisplayName = "Reversed",
            Category = "Test",
            Scope = JobScope.System,
            ConcurrencyScope = ConcurrencyScope.PerTarget,
            LockPartitionFields = ["b", "a"]
        };
        IJobManager subject = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { ordered, reversed }),
            Mock.Of<IJobExecutionRepository>(),
            Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLogger<JobManager>.Instance);
        var parameters = new Dictionary<string, string?> { ["a"] = "1", ["b"] = "2" };

        // Act
        var orderedPartition = subject.ResolveLockPartition(ordered.JobType, parameters);
        var reversedPartition = subject.ResolveLockPartition(reversed.JobType, parameters);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(orderedPartition, Is.EqualTo(reversedPartition));
            Assert.That(orderedPartition, Is.EqualTo("a=1&b=2"));
        }
    }

    [TestCase(null)]
    [TestCase("   ")]
    public void ResolveLockPartition_UniqueResult_WhenTargetMissingOrBlank(string? value)
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "magpie.job",
            DisplayName = "Magpie job",
            Category = "Test",
            Scope = JobScope.System,
            ConcurrencyScope = ConcurrencyScope.PerTarget,
            LockPartitionFields = ["contentItemId"]
        };
        IJobManager subject = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            Mock.Of<IJobExecutionRepository>(),
            Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLogger<JobManager>.Instance);
        var parameters = value is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["contentItemId"] = value };

        // Act
        var first = subject.ResolveLockPartition(spec.JobType, parameters);
        var second = subject.ResolveLockPartition(spec.JobType, parameters);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo("type:magpie.job"));
        }
    }
}
