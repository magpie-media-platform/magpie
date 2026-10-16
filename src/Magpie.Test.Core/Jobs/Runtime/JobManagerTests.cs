using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Magpie.Models.Filtering;
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
public sealed class JobManagerTests
{
    private const string TraceSourceName = "Magpie.Test.Core.Jobs.Runtime.JobManagerTests";

    private const string PublisherTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private const string InvalidMinuteMessage =
        "The given cron expression has an invalid format. Minutes: Value must be a number between 0 and 59 " +
        "(all inclusive).";

    private Mock<IJobRuntime> _runtime = null!;
    private Mock<IJobExecutionRepository> _executionRepository = null!;
    private Mock<IScheduleTriggerRepository> _scheduleTriggerRepository = null!;
    private Mock<IEventTriggerRepository> _eventTriggerRepository = null!;
    private Mock<IJobExecutionScopeFactory> _jobExecutionScopes = null!;
    private FakeTimeProvider _timeProvider = null!;
    private Mock<ITransactionManager> _transactions = null!;
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
        _transactions = new Mock<ITransactionManager>();
        _transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));

        _subject = new JobManager(
            _runtime.Object,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _jobExecutionScopes.Object,
            _timeProvider,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);
    }

    [Test]
    public async Task ExecuteAsync_CreatesExecutionAndReturnsId_WhenTheRequestIsValid()
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOwnerWithoutRunning_WhenOnlyIfInactiveIsRequested()
    {
        // Arrange
        _executionRepository.Setup(repository => repository.CreateIfInactiveAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecution
            {
                Id = new Guid("00000000-0000-0000-0000-000000000001"),
                JobType = "TestJob",
                Status = JobExecutionStatus.Queued,
                NotBeforeAt = new DateTime(2026, 8, 4, 12, 30, 0, DateTimeKind.Utc)
            });

        // Act
        var result = await _subject.ExecuteAsync(new JobEnqueueRequest
        {
            JobType = "TestJob",
            Mode = JobExecutionMode.Queue,
            OnlyIfInactive = true
        }).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(new Guid("00000000-0000-0000-0000-000000000001")));
        _executionRepository.Verify(repository => repository.CreateIfInactiveAsync(
            It.Is<JobExecution>(execution => execution.LockPartition == "type:TestJob"), null,
            It.IsAny<CancellationToken>()), Times.Once);
        _executionRepository.Verify(repository => repository.CreateAsync(
            It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _runtime.Verify(runtime => runtime.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void ExecuteAsync_RejectsImmediateMode_WhenOnlyIfInactiveIsRequested()
    {
        // Arrange
        var request = new JobEnqueueRequest
        {
            JobType = "TestJob",
            Mode = JobExecutionMode.Immediate,
            OnlyIfInactive = true
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await _subject.ExecuteAsync(request).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("An inactive-only request must use queued execution."));
        _executionRepository.VerifyNoOtherCalls();
        _runtime.VerifyNoOtherCalls();
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_LeavesParentNullAndStartsANewCorrelation_WhenTheRequestIsASuccessor()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var parentCorrelationId = Guid.NewGuid();
        JobExecution? captured = null;
        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((value, _, _) => captured = value)
            .ReturnsAsync(new JobExecution
            {
                Id = Guid.NewGuid(),
                JobType = "PollingJob",
                Status = JobExecutionStatus.Queued
            });
        _jobExecutionScopes.SetupGet(value => value.CurrentExecutionId).Returns(parentId);
        _jobExecutionScopes.SetupGet(value => value.CurrentCorrelationId).Returns(parentCorrelationId);

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "PollingJob",
                Mode = JobExecutionMode.Queue,
                IsSuccessor = true,
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured?.ParentExecutionId, Is.Null);
            Assert.That(captured?.CorrelationId, Is.Not.Null);
            Assert.That(captured?.CorrelationId, Is.Not.EqualTo(parentCorrelationId));
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheParentAndTheCorrelation_WhenTheRequestIsNotASuccessor()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        JobExecution? captured = null;
        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((value, _, _) => captured = value)
            .ReturnsAsync(new JobExecution
            {
                Id = Guid.NewGuid(),
                JobType = "ChildJob",
                Status = JobExecutionStatus.Queued
            });
        _jobExecutionScopes.SetupGet(value => value.CurrentExecutionId).Returns(parentId);

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "ChildJob",
                Mode = JobExecutionMode.Queue,
                CorrelationId = correlationId,
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured?.ParentExecutionId, Is.EqualTo(parentId));
            Assert.That(captured?.CorrelationId, Is.EqualTo(correlationId));
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsThePublisherTrace_WhenTheTriggerContextAlreadyCarriesOne()
    {
        // Arrange
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == TraceSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource(TraceSourceName);
        JobExecution? captured = null;
        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((value, _, _) => captured = value)
            .ReturnsAsync(new JobExecution
            {
                Id = Guid.NewGuid(),
                JobType = "ChildJob",
                Status = JobExecutionStatus.Queued
            });
        using var activity = source.StartActivity("dispatcher");

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "ChildJob",
                Mode = JobExecutionMode.Queue,
                TriggerContext = new Dictionary<string, string>
                {
                    ["traceparent"] = PublisherTraceParent
                }
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(captured?.TriggerContext["traceparent"], Is.EqualTo(PublisherTraceParent));
    }

    [Test]
    public async Task ExecuteAsync_InjectsTheCurrentTrace_WhenTheTriggerContextCarriesNone()
    {
        // Arrange
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == TraceSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource(TraceSourceName);
        JobExecution? captured = null;
        _executionRepository
            .Setup(m => m.CreateAsync(
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((value, _, _) => captured = value)
            .ReturnsAsync(new JobExecution
            {
                Id = Guid.NewGuid(),
                JobType = "ChildJob",
                Status = JobExecutionStatus.Queued
            });
        using var activity = source.StartActivity("dispatcher");
        var traceId = activity!.TraceId.ToHexString();

        // Act
        await _subject.ExecuteAsync(
            new JobEnqueueRequest { JobType = "ChildJob", Mode = JobExecutionMode.Queue },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(captured?.TriggerContext["traceparent"], Does.Contain(traceId));
    }

    [Test]
    public async Task ExecuteAsync_LeavesParentNull_WhenTheTriggerHasNoParent()
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(new JobEnqueueRequest { JobType = "TopLevelJob", Mode = JobExecutionMode.Queue })
            .ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.ParentExecutionId == null),
                null,
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        var options = new JobRuntimeOptions();
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            TimeProvider.System,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
            Assert.That(scheduler.Total, Is.EqualTo(50));
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(new JobEnqueueRequest { JobType = "UnknownJob", Mode = JobExecutionMode.Queue })
            .ConfigureAwait(false);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.Scope == JobScope.System),
                null,
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
                It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
            .Setup(repository => repository.GetLineageAsync(executionId, 500, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync([execution]);

        // Act
        var result = await _subject
            .GetExecutionTreeAsync(executionId, includeSiblings: false, maxNodes: 500)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result!.Root!.Status, Is.EqualTo(expectedStatus));
    }

    [Test]
    public async Task GetExecutionTreeAsync_ReportsTruncation_WhenTheLineageFillsTheNodeLimit()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        _executionRepository
            .Setup(repository => repository.GetLineageAsync(rootId, 500, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new JobExecution { Id = rootId, JobType = "TestJob", Status = JobExecutionStatus.Running },
                new JobExecution
                {
                    Id = childId,
                    ParentExecutionId = rootId,
                    JobType = "TestJob",
                    Status = JobExecutionStatus.Running
                }
            ]);

        // Act
        var result = await _subject
            .GetExecutionTreeAsync(rootId, includeSiblings: false, maxNodes: 2)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Truncated, Is.True);
            Assert.That(result.TotalCount, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetExecutionTreeAsync_ClampsTheNodeLimit_WhenTheRequestExceedsTheCeiling()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        _executionRepository
            .Setup(repository => repository.GetLineageAsync(executionId, 500, 5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new JobExecution { Id = executionId, JobType = "TestJob" }]);

        // Act
        var result = await _subject
            .GetExecutionTreeAsync(executionId, includeSiblings: false, maxNodes: 1000000)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.TotalCount, Is.EqualTo(1));
            Assert.That(result.Truncated, Is.False);
        }
    }

    [Test]
    public async Task CancelAsync_DelegatesToRuntime_WhenCancellationIsRequested()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        _runtime
            .Setup(m => m.TryCancelAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.CancelAsync(executionId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _runtime.Verify(m => m.TryCancelAsync(executionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CancelAsync_ReturnsFalse_WhenRuntimeReturnsFalse()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        _runtime
            .Setup(m => m.TryCancelAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _subject.CancelAsync(executionId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_ComputesNextRun_WhenTheScheduleIsCreated()
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
    public void CreateScheduleTriggerAsync_Throws_WhenRequiredParameterIsMissing()
    {
        // Arrange
        var create = new ScheduleTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
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
                    SupportedTriggerModes = new[] { JobTriggerMode.Schedule },
                    Parameters = new[]
                    {
                        new JobParameter("contentHierarchyItemId", null, DataType.String, true, null),
                    },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.CreateScheduleTriggerAsync(create, Guid.NewGuid(), Role.Admin, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("Required parameter contentHierarchyItemId is missing."));
        _scheduleTriggerRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<ScheduleTrigger>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_PreservesOmittedParameter_WhenRequiredDefaultIsValid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var create = new ScheduleTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
        };
        _scheduleTriggerRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ScheduleTrigger>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = Guid.NewGuid(),
                JobType = "TestJob",
                Scope = JobScope.Administrative,
                Name = "TestTrigger",
                Schedule = "0 * * * *",
            });
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
                    SupportedTriggerModes = new[] { JobTriggerMode.Schedule },
                    Parameters = new[] { new JobParameter("retries", null, DataType.Number, true, "3") },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        await subject.CreateScheduleTriggerAsync(create, userId, Role.Admin, default).ConfigureAwait(false);

        // Assert
        _scheduleTriggerRepository.Verify(
            repository => repository.CreateAsync(
                It.Is<ScheduleTrigger>(trigger => trigger.Parameters.Count == 0),
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act and Assert
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
    public void CreateScheduleTriggerAsync_ThrowsMagpieExceptionWithCleanMessage_WhenScheduleIsInvalid()
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
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
            Assert.That(exception!.Message, Is.EqualTo(InvalidMinuteMessage));
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

        // Act
        var result = await _subject.PatchScheduleTriggerAsync(triggerId, patch, userId, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _scheduleTriggerRepository.Verify(
            r => r.PatchAsync(
                It.Is<ScheduleTriggerPatch>(applied =>
                    applied.Id == triggerId && applied.NextRunAt!.Value.HasValue),
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PatchScheduleTriggerAsync_ReturnsFalse_WhenTheRepositoryPatchFails()
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
            Schedule = new() { Value = "*/10 * * * *" }
        };

        _scheduleTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trigger);
        _scheduleTriggerRepository
            .Setup(m => m.PatchAsync(It.IsAny<ScheduleTriggerPatch>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _subject.PatchScheduleTriggerAsync(triggerId, patch, userId, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PatchScheduleTriggerAsync_ThrowsMagpieExceptionWithCleanMessage_WhenScheduleIsInvalid()
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
        var exception = Assert.ThrowsAsync<MagpieException>(
            async () => await _subject.PatchScheduleTriggerAsync(
                triggerId,
                patch,
                Guid.NewGuid(),
                callerRole: Role.Admin)
                .ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message, Is.EqualTo(InvalidMinuteMessage));
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

        // Act
        var result = await _subject.PatchScheduleTriggerAsync(triggerId, patch, userId, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _scheduleTriggerRepository.Verify(
            r => r.PatchAsync(
                It.Is<ScheduleTriggerPatch>(applied =>
                    applied.Id == triggerId && !applied.NextRunAt!.Value.HasValue),
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void PatchScheduleTriggerAsync_Throws_WhenEffectiveParametersMissRequiredValue()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _scheduleTriggerRepository
            .Setup(repository => repository.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = triggerId,
                JobType = "TestJob",
                Scope = JobScope.Administrative,
                Name = "Test trigger",
                Schedule = "0 * * * *",
                Enabled = true,
                Parameters = new Dictionary<string, string>
                {
                    ["contentHierarchyItemId"] = "item-1",
                    ["unchanged"] = "value",
                },
            });
        var patch = new ScheduleTriggerPatch
        {
            Parameters = new()
            {
                Remove = ["contentHierarchyItemId"],
                Update = new Dictionary<string, string> { ["unchanged"] = "updated" },
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
                    SupportedTriggerModes = new[] { JobTriggerMode.Schedule },
                    Parameters = new[]
                    {
                        new JobParameter("contentHierarchyItemId", null, DataType.String, true, null),
                    },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.PatchScheduleTriggerAsync(triggerId, patch, userId, callerRole: Role.Admin, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("Required parameter contentHierarchyItemId is missing."));
        _scheduleTriggerRepository.Verify(
            repository => repository.PatchAsync(
                It.IsAny<ScheduleTriggerPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void PatchScheduleTriggerAsync_Throws_WhenLegacyInvalidTriggerIsEnabled()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _scheduleTriggerRepository
            .Setup(repository => repository.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = triggerId,
                JobType = "TestJob",
                Scope = JobScope.Administrative,
                Name = "Legacy trigger",
                Schedule = "0 * * * *",
                Enabled = false,
            });
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
                    SupportedTriggerModes = new[] { JobTriggerMode.Schedule },
                    Parameters = new[]
                    {
                        new JobParameter("contentHierarchyItemId", null, DataType.String, true, null),
                    },
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.PatchScheduleTriggerAsync(
                triggerId,
                new ScheduleTriggerPatch { Enabled = new() { Value = true } },
                userId,
                callerRole: Role.Admin,
                default).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("Required parameter contentHierarchyItemId is missing."));
        _scheduleTriggerRepository.Verify(
            repository => repository.PatchAsync(
                It.IsAny<ScheduleTriggerPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var result = await subject.CreateEventTriggerAsync(create, userId, Role.Admin, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestEventTrigger"));
    }

    [TestCase("${event.name}", true)]
    [TestCase("before ${event.name} after", true)]
    [TestCase("${event.name", true)]
    [TestCase("$${event.name}", false)]
    [TestCase("plain", false)]
    public void ContainsEventBinding_ReturnsWhetherTheScanMeetsABinding_WhenTheValueIsScanned(
        string value,
        bool expected)
    {
        // Act
        var contains = JobManager.ContainsEventBinding(value);

        // Assert
        Assert.That(contains, Is.EqualTo(expected));
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.PatchEventTriggerAsync(triggerId, patch, userId, callerRole: Role.Admin, default)
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
    public void CreateEventTriggerAsync_Throws_WhenEventFilterRegexIsMalformed()
    {
        // Arrange
        var create = new EventTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = new EventFilter
            {
                EventName = new()
                {
                    Operator = CriteriaValueOperator.Regex,
                    Value = "[",
                },
            },
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
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.CreateEventTriggerAsync(create, Guid.NewGuid(), Role.Admin, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Event filter regular expression is malformed."));
        _eventTriggerRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<EventTrigger>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task CreateEventTriggerAsync_DelegatesToRepository_WhenEventFilterRegexIsValid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var eventFilter = new EventFilter
        {
            EventName = new()
            {
                Operator = CriteriaValueOperator.Regex,
                Value = "^content\\.[a-z]+$",
            },
        };
        var create = new EventTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = eventFilter,
        };
        _eventTriggerRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<EventTrigger>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = Guid.NewGuid(),
                JobType = "TestJob",
                Scope = JobScope.Administrative,
                Name = "TestEventTrigger",
                EventFilter = eventFilter,
            });
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var result = await subject.CreateEventTriggerAsync(create, userId, Role.Admin, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestEventTrigger"));
    }

    [Test]
    public void CreateEventTriggerAsync_Throws_WhenEventContextOperatorIsUnsupported()
    {
        // Arrange
        var create = new EventTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = new EventFilter
            {
                EventContext = new()
                {
                    Items = new Dictionary<string, PropertyCriterion<string>>
                    {
                        ["content.itemId"] = new()
                        {
                            Operator = CriteriaValueOperator.GreaterThan,
                            Value = "100",
                        },
                    },
                },
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
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.CreateEventTriggerAsync(create, Guid.NewGuid(), Role.Admin, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("Event filter field eventContext.content.itemId uses unsupported operator GreaterThan."));
        _eventTriggerRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<EventTrigger>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase(CriteriaValueOperator.Equals)]
    [TestCase(CriteriaValueOperator.NotEquals)]
    [TestCase(CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.NotContainsString)]
    [TestCase(CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString)]
    [TestCase(CriteriaValueOperator.In)]
    [TestCase(CriteriaValueOperator.NotIn)]
    [TestCase(CriteriaValueOperator.Regex)]
    [TestCase(CriteriaValueOperator.IsNull)]
    [TestCase(CriteriaValueOperator.IsNotNull)]
    public async Task CreateEventTriggerAsync_AcceptsNullOperands_WhenOperatorSupportsNull(
        CriteriaValueOperator filterOperator)
    {
        // Arrange
        var create = new EventTriggerCreate
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "TestEventTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Operator = filterOperator },
                EventContext = new()
                {
                    Items = new Dictionary<string, PropertyCriterion<string>>
                    {
                        ["optionalField"] = new() { Operator = filterOperator }
                    }
                },
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
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        _eventTriggerRepository.Setup(repository => repository.CreateAsync(It.IsAny<EventTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync((EventTrigger trigger,
                Guid? _, CancellationToken _) => trigger);

        // Act
        await subject.CreateEventTriggerAsync(create, Guid.NewGuid(), Role.Admin, default)
            .ConfigureAwait(false);

        // Assert
        _eventTriggerRepository.Verify(
            repository => repository.CreateAsync(
                It.Is<EventTrigger>(trigger => trigger.EventFilter == create.EventFilter),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase(CriteriaValueOperator.Equals, false)]
    [TestCase(CriteriaValueOperator.NotEquals, false)]
    [TestCase(CriteriaValueOperator.Equals, true)]
    [TestCase(CriteriaValueOperator.NotEquals, true)]
    public async Task PatchEventTriggerAsync_AcceptsNullComparison_WhenEnabledTriggerIsPatched(
        CriteriaValueOperator filterOperator, bool replaceFilter)
    {
        // Arrange
        var filter = new EventFilter
        {
            EventContext = new()
            {
                Items = new Dictionary<string, PropertyCriterion<string>>
                {
                    ["optionalField"] = new() { Operator = filterOperator }
                }
            }
        };
        var trigger = new EventTrigger
        {
            Id = Guid.Empty,
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "Original",
            Enabled = true,
            EventFilter = filter
        };
        _eventTriggerRepository.Setup(repository => repository.GetByIdAsync(Guid.Empty,
            It.IsAny<CancellationToken>())).ReturnsAsync(trigger);
        _eventTriggerRepository.Setup(repository => repository.PatchAsync(It.IsAny<EventTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var patch = new EventTriggerPatch
        {
            Name = new() { Value = "Renamed" },
            EventFilter = replaceFilter ? new() { Value = filter } : null
        };

        // Act
        var result = await _subject.PatchEventTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _eventTriggerRepository.Verify(repository => repository.PatchAsync(patch,
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void PatchEventTriggerAsync_Throws_WhenEventFilterRegexIsMalformed()
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
            });
        var patch = new EventTriggerPatch
        {
            EventFilter = new()
            {
                Value = new EventFilter
                {
                    EventContext = new()
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["itemId"] = new()
                            {
                                Operator = CriteriaValueOperator.Regex,
                                Value = "(",
                            },
                        },
                    },
                },
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
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.PatchEventTriggerAsync(triggerId, patch, userId, callerRole: Role.Admin, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Event filter regular expression is malformed."));
        _eventTriggerRepository.Verify(
            repository => repository.PatchAsync(
                It.IsAny<EventTriggerPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void PatchEventTriggerAsync_Throws_WhenLegacyInvalidTriggerIsEnabled()
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
                Name = "Legacy event trigger",
                Enabled = false,
                EventFilter = new EventFilter
                {
                    EventName = new()
                    {
                        Operator = CriteriaValueOperator.ContainsWord,
                        Value = "content",
                    },
                },
            });
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
                }
            }),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await subject.PatchEventTriggerAsync(
                triggerId,
                new EventTriggerPatch { Enabled = new() { Value = true } },
                userId,
                callerRole: Role.Admin,
                default).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("Event filter field eventName uses unsupported operator ContainsWord."));
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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
                new JobRuntimeOptions(),
                NullLogger<JobRegistry>.Instance),
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
            NullLoggerFactory.Instance.CreateLogger<JobManager>(), _transactions.Object);

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
    public void ResolveLockPartition_ReturnsTheScopePartition_WhenScopeVaries(ConcurrencyScope scope, string? expected)
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
            NullLogger<JobManager>.Instance, _transactions.Object);
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
            NullLogger<JobManager>.Instance, _transactions.Object);
        var parameters = new Dictionary<string, string?> { ["contentItemId"] = "abc-123" };

        // Act
        var searchPartition = subject.ResolveLockPartition(search.JobType, parameters);
        var refreshPartition = subject.ResolveLockPartition(refresh.JobType, parameters);

        // Assert
        Assert.That(searchPartition == refreshPartition, Is.EqualTo(expectedSame));
    }

    [TestCase(false, " value= {ABCDEFFF-1234-5678-90AB-CDEF12345678} ")]
    [TestCase(true, " value=abcdefff-1234-5678-90ab-cdef12345678")]
    public void ResolveLockPartition_NormalizesOnlyDeclaredGuids_WhenTargetContainsGuid(bool guidField, string expected)
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "magpie.job",
            DisplayName = "Magpie job",
            Category = "Test",
            Scope = JobScope.System,
            ConcurrencyScope = ConcurrencyScope.PerTarget,
            LockPartitionFields = [" value"],
            GuidLockPartitionFields = guidField ? [" value"] : []
        };
        IJobManager subject = new JobManager(Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            Mock.Of<IJobExecutionRepository>(), Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(), Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System, NullLogger<JobManager>.Instance,
            _transactions.Object);
        var parameters = new Dictionary<string, string?>
        {
            [" value"] = " {ABCDEFFF-1234-5678-90AB-CDEF12345678} "
        };

        // Act
        var partition = subject.ResolveLockPartition(spec.JobType, parameters);

        // Assert
        Assert.That(partition, Is.EqualTo(expected));
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
            NullLogger<JobManager>.Instance, _transactions.Object);
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
            NullLogger<JobManager>.Instance, _transactions.Object);
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
