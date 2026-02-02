using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public class JobManagerTests
{
    private Mock<IJobRuntime> _runtime = null!;
    private Mock<IJobExecutionRepository> _executionRepository = null!;
    private Mock<IScheduleTriggerRepository> _scheduleTriggerRepository = null!;
    private Mock<IEventTriggerRepository> _eventTriggerRepository = null!;
    private IJobManager _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _runtime = new Mock<IJobRuntime>();
        _executionRepository = new Mock<IJobExecutionRepository>();
        _scheduleTriggerRepository = new Mock<IScheduleTriggerRepository>();
        _eventTriggerRepository = new Mock<IEventTriggerRepository>();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

        _subject = new JobManager(
            _runtime.Object,
            _executionRepository.Object,
            _scheduleTriggerRepository.Object,
            _eventTriggerRepository.Object,
            loggerFactory.CreateLogger<JobManager>());
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
            .Setup(m => m.CreateAsync(It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        var result = await _subject.ExecuteAsync(
            "TestJob",
            null,
            null,
            JobExecutionMode.Queue,
            null);

        // Assert
        Assert.That(result, Is.EqualTo(executionId));
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.JobType == "TestJob" && e.Status == JobExecutionStatus.Queued),
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
            .Setup(m => m.CreateAsync(It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(
            "TestJob",
            null,
            null,
            JobExecutionMode.Immediate,
            null);

        // Assert
        _runtime.Verify(m => m.ExecuteAsync(executionId, It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(m => m.CreateAsync(It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(
            "TestJob",
            null,
            null,
            JobExecutionMode.Queue,
            null);

        // Assert
        _runtime.Verify(m => m.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_PassesParameters()
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
            .Setup(m => m.CreateAsync(It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(
            "TestJob",
            null,
            parameters,
            JobExecutionMode.Queue,
            null);

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
    public async Task ExecuteAsync_SetsExtensionId()
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
            .Setup(m => m.CreateAsync(It.IsAny<JobExecution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        await _subject.ExecuteAsync(
            "TestJob",
            extensionId,
            null,
            JobExecutionMode.Queue,
            null);

        // Assert
        _executionRepository.Verify(
            r => r.CreateAsync(
                It.Is<JobExecution>(e => e.ExtensionId == extensionId),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task QueryExecutionsAsync_DelegatesToRepository()
    {
        // Arrange
        var query = new JobExecutionQuery();
        var paging = new PagingParameter(1, 10);
        var executions = new PagedList<JobExecution>([], 1, 10, 0);

        _executionRepository
            .Setup(m => m.QueryAsync(query, paging, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(executions);

        // Act
        var result = await _subject.QueryExecutionsAsync(query, paging, null);

        // Assert
        Assert.That(result, Is.SameAs(executions));
    }

    [Test]
    public async Task GetExecutionAsync_DelegatesToRepository()
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
        var result = await _subject.GetExecutionAsync(executionId);

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
        var result = await _subject.GetExecutionAsync(executionId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RequestCancellationAsync_DelegatesToRuntime()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        _runtime
            .Setup(m => m.TryCancelAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.RequestCancellationAsync(executionId);

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
        var result = await _subject.RequestCancellationAsync(executionId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_DelegatesToRepository()
    {
        // Arrange
        var create = new ScheduleTriggerCreate
        {
            ExtensionId = null,
            JobType = "TestJob",
            Scope = JobScope.System,
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
            Scope = JobScope.System,
            Name = "TestTrigger",
            Schedule = "0 * * * *"
        };

        _scheduleTriggerRepository
            .Setup(m => m.CreateAsync(It.IsAny<ScheduleTrigger>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTrigger);

        // Act
        var result = await _subject.CreateScheduleTriggerAsync(create, userId);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestTrigger"));
        _scheduleTriggerRepository.Verify(
            r => r.CreateAsync(
                It.Is<ScheduleTrigger>(t => t.Name == "TestTrigger" && t.Schedule == "0 * * * *"),
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateEventTriggerAsync_DelegatesToRepository()
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
            Scope = JobScope.System,
            Name = "TestEventTrigger",
            EventFilter = eventFilter,
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true
        };

        var userId = Guid.NewGuid();

        var createdTrigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Scope = JobScope.System,
            Name = "TestEventTrigger",
            EventFilter = eventFilter
        };

        _eventTriggerRepository
            .Setup(m => m.CreateAsync(It.IsAny<EventTrigger>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTrigger);

        // Act
        var result = await _subject.CreateEventTriggerAsync(create, userId);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestEventTrigger"));
    }
}
