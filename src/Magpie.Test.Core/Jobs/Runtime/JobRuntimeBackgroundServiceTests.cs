using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Events;
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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class JobRuntimeBackgroundServiceTests
{
    private DatabaseMigrationState _migrationState = null!;

    private const string LocalNodeId = "node-A";

    private Mock<IAuditRecordRepository> _audits = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private Mock<IJobManager> _jobManager = null!;
    private JobRuntimeOptions _options = null!;
    private Mock<IJobRetryPolicy> _retryPolicy = null!;
    private Mock<IJobRuntime> _runtime = null!;
    private Mock<IScheduleTriggerRepository> _scheduleTriggers = null!;
    private Mock<IServiceScopeFactory> _scopeFactory = null!;
    private MagpieHostEnvironment _hostEnvironment = null!;
    private JobSpec _spec = null!;
    private JobShutdownSignal _shutdownSignal = null!;
    private JobRuntimeBackgroundService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _migrationState = new DatabaseMigrationState();
        _migrationState.Complete();
        _audits = new Mock<IAuditRecordRepository> { CallBase = true };
        _executions = new Mock<IJobExecutionRepository> { CallBase = true };
        _jobManager = new Mock<IJobManager>();
        _retryPolicy = new Mock<IJobRetryPolicy>();
        _runtime = new Mock<IJobRuntime>();
        _scheduleTriggers = new Mock<IScheduleTriggerRepository> { CallBase = true };
        _options = new JobRuntimeOptions
        {
            StaleJobCleanupInterval = TimeSpan.FromHours(1),
            TimedOutJobReapInterval = TimeSpan.FromSeconds(30)
        };

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(_scheduleTriggers.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionNodeStatusRepository)))
            .Returns(Mock.Of<IExtensionNodeStatusRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IUserRepository)))
            .Returns(Mock.Of<IUserRepository>(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()) == Task.FromResult(new PagedList<User>())));
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(() => _executions.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IAuditRecordRepository)))
            .Returns(_audits.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobRetryPolicy)))
            .Returns(_retryPolicy.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobManager)))
            .Returns(_jobManager.Object);
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<JobExecution?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<JobExecution?>> operation, CancellationToken token) =>
                operation(token));
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));
        serviceProvider
            .Setup(provider => provider.GetService(typeof(ITransactionManager)))
            .Returns(transactionManager.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(scope => scope.ServiceProvider).Returns(serviceProvider.Object);

        _scopeFactory = new Mock<IServiceScopeFactory>();
        _scopeFactory.Setup(factory => factory.CreateScope()).Returns(serviceScope.Object);

        _hostEnvironment = new MagpieHostEnvironment(
            LocalNodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);

        _spec = new JobSpec
        {
            JobType = "ReleaseSearchItem",
            DisplayName = "Release search item",
            Category = "Test",
            Scope = JobScope.System,
            FailurePolicy = JobFailurePolicy.Requeue
        };

        _shutdownSignal = new JobShutdownSignal();

        _subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
            _runtime.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
    }

    [TearDown]
    public void TearDown()
    {
        _subject.Dispose();
    }

    [TestCase(JobExecutionMode.Queue)]
    [TestCase(JobExecutionMode.Immediate)]
    public async Task RunOnceAsync_PreservesUnavailableTriggersWithoutExecutions_WhenOtherTriggersCanRun(
        JobExecutionMode unavailableMode)
    {
        // Arrange
        var dueAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var unavailable = new ScheduleTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "UnloadedExtensionJob",
            Name = "Unavailable job",
            Scope = JobScope.System,
            Schedule = "* * * * *",
            Enabled = true,
            NextRunAt = dueAt,
            ExecutionMode = unavailableMode
        };
        var available = new ScheduleTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Name = "Available job",
            Scope = JobScope.System,
            Schedule = "* * * * *",
            Enabled = true,
            NextRunAt = dueAt,
            ExecutionMode = JobExecutionMode.Queue
        };
        _scheduleTriggers
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ScheduleTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ScheduleTrigger> { Items = [unavailable, available], TotalItems = 2 });
        _scheduleTriggers
            .Setup(repository => repository.TryClaimNextRunAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _executions
            .Setup(repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobExecution execution, Guid? _, CancellationToken _) => execution);

        // Act
        await _subject.RunOnceAsync(dueAt, default).ConfigureAwait(false);
        await _subject.RunOnceAsync(dueAt.AddMinutes(1), default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unavailable.Enabled, Is.True);
            Assert.That(unavailable.NextRunAt, Is.EqualTo(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
            _scheduleTriggers.Verify(repository => repository.QueryAsync(
                It.IsAny<ScheduleTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            _scheduleTriggers.Verify(repository => repository.QueryAllAsync(
                It.IsAny<ScheduleTriggerQuery>(),
                null,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            _scheduleTriggers.Verify(repository => repository.TryClaimNextRunAsync(
                available.Id,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            _scheduleTriggers.VerifyNoOtherCalls();
            _executions.Verify(repository => repository.CreateAsync(
                It.Is<JobExecution>(execution => execution.ScheduleTriggerId == available.Id),
                null,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            _executions.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotInitializeTheRuntime_WhenTheDatabaseWorkIsPending()
    {
        // Arrange
        var migrationState = new DatabaseMigrationState();
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
            _runtime.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            migrationState);

        // Act
        await subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subject.ExecuteTask!.IsCompleted, Is.False);
            _runtime.Verify(
                runtime => runtime.InitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_InitializesTheRuntime_WhenTheDatabaseWorkCompletes()
    {
        // Arrange
        var initialized = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _runtime
            .Setup(runtime => runtime.InitializeAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                initialized.TrySetResult(null);
                return Task.CompletedTask;
            });
        var migrationState = new DatabaseMigrationState();
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
            _runtime.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            migrationState);
        await subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        migrationState.Complete();

        // Assert
        Assert.That(
            async () => await initialized.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
            Throws.Nothing);
    }

    [Test]
    public void JobRuntimeOptions_UsesSeparateReapIntervals_WhenConstructed()
    {
        // Arrange
        var options = new JobRuntimeOptions();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.TimedOutJobReapInterval, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.StaleJobCleanupInterval, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(options.TimedOutJobReapInterval, Is.LessThan(options.StaleJobCleanupInterval));
        }
    }

    [Test]
    public async Task RunTimedOutJobReapOnceAsync_WritesOneAudit_WhenRepeatedQueryReturnsSameExecution()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var timedOut = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            TimeoutAt = DateTime.UtcNow.AddMinutes(-1),
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30)
        };

        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.TimeoutAt != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [timedOut], TotalItems = 1 });
        _executions
            .SetupSequence(repository => repository.TryMarkRunningFailedAsync(
                executionId,
                It.Is<string>(reason => reason.Contains("Exceeded maximum run time", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        // Act
        await _subject.RunTimedOutJobReapOnceAsync(default).ConfigureAwait(false);
        await _subject.RunTimedOutJobReapOnceAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.TryMarkRunningFailedAsync(
                executionId,
                It.Is<string>(reason => reason.Contains("Exceeded maximum run time", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _audits.Verify(
            repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "JobTimeout"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunTimedOutJobReapOnceAsync_DoesNotRetry_WhenCancellationWinsTransitionRace()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var timedOut = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            TimeoutAt = DateTime.UtcNow.AddMinutes(-1),
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30)
        };

        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.TimeoutAt != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [timedOut], TotalItems = 1 });
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                executionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _subject.RunTimedOutJobReapOnceAsync(default).ConfigureAwait(false);

        // Assert
        _retryPolicy.Verify(
            policy => policy.BuildRetryAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _executions.Verify(
            repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunTimedOutJobReapOnceAsync_EnqueuesRetry_WhenStoppingBeginsAfterReclaim()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var retry = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            Attempt = 2
        };
        var timedOut = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            TimeoutAt = DateTime.UtcNow.AddMinutes(-1),
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30)
        };
        using var stopping = new CancellationTokenSource();

        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.TimeoutAt != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [timedOut], TotalItems = 1 });
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                executionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => stopping.Cancel())
            .ReturnsAsync(true);
        _retryPolicy
            .Setup(policy => policy.BuildRetryAsync(timedOut, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobExecution execution, CancellationToken cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return retry;
            });
        _executions
            .Setup(repository => repository.CreateAsync(retry, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retry);

        // Act
        await _subject.RunTimedOutJobReapOnceAsync(stopping.Token).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.CreateAsync(
                retry,
                null,
                It.Is<CancellationToken>(token => !token.IsCancellationRequested)),
            Times.Once);
    }

    [Test]
    public async Task CleanupPasses_EnqueueOneRetry_WhenTimeoutAndStaleReclaimRace()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var retry = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            Attempt = 2
        };
        var candidate = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            TimeoutAt = DateTime.UtcNow.AddMinutes(-1),
            LastHeartbeat = DateTime.UtcNow.AddHours(-2)
        };
        var transitionCount = 0;

        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running &&
                    (query.TimeoutAt != null || query.LastHeartbeat != null)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [candidate], TotalItems = 1 });
        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.Status != JobExecutionStatus.Running),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                executionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref transitionCount) == 1);
        _retryPolicy
            .Setup(policy => policy.BuildRetryAsync(candidate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retry);
        _executions
            .Setup(repository => repository.CreateAsync(retry, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retry);

        // Act
        await Task.WhenAll(
            _subject.RunTimedOutJobReapOnceAsync(default),
            _subject.RunCleanupOnceAsync(default)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitionCount, Is.EqualTo(2));
            _retryPolicy.Verify(policy => policy.BuildRetryAsync(candidate, It.IsAny<CancellationToken>()), Times.Once);
            _executions.Verify(
                repository => repository.CreateAsync(
                    retry,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task StartAsync_RepeatsTimedOutQuery_WhenOnlyTheTimedOutIntervalElapses()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 25, 5, 0, 0, TimeSpan.Zero));
        var firstReap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reapCount = 0;
        var executions = new Mock<IJobExecutionRepository> { CallBase = true };
        executions
            .Setup(repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.TimeoutAt != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                var count = Interlocked.Increment(ref reapCount);
                if (count == 1)
                {
                    firstReap.TrySetResult();
                }
                else if (count == 2)
                {
                    secondReap.TrySetResult();
                }
            })
            .ReturnsAsync(new PagedList<JobExecution>());

        var scheduleTriggers = new Mock<IScheduleTriggerRepository> { CallBase = true };
        scheduleTriggers
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ScheduleTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ScheduleTrigger>());

        var extensionNodes = new Mock<IExtensionNodeStatusRepository> { CallBase = true };
        extensionNodes
            .Setup(repository => repository.GetByNodeIdAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtensionNodeStatus?)null);
        var users = new Mock<IUserRepository> { CallBase = true };
        users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "system"),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User> { Items = [], TotalItems = 0 });

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(executions.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IAuditRecordRepository)))
            .Returns(Mock.Of<IAuditRecordRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(scheduleTriggers.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionNodeStatusRepository)))
            .Returns(extensionNodes.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IUserRepository)))
            .Returns(users.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(ITransactionManager)))
            .Returns(Mock.Of<ITransactionManager>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobRetryPolicy)))
            .Returns(Mock.Of<IJobRetryPolicy>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobManager)))
            .Returns(Mock.Of<IJobManager>());
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(scope => scope.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(serviceScope.Object);

        var runtime = new Mock<IJobRuntime>();
        runtime
            .Setup(jobRuntime => jobRuntime.InitializeAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var concurrencyScheduler = new Mock<IJobConcurrencyScheduler>();
        concurrencyScheduler.Setup(scheduler => scheduler.HasAdmissionCapacity()).Returns(false);
        var hostEnvironment = new MagpieHostEnvironment(
            LocalNodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
            runtime.Object,
            concurrencyScheduler.Object,
            _options,
            hostEnvironment,
            scopeFactory.Object,
            eventBus,
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            timeProvider,
            databaseMigration: _migrationState);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await firstReap.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        timeProvider.Advance(_options.TimedOutJobReapInterval);
        await secondReap.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        executions.Verify(
            repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.TimeoutAt != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        executions.Verify(
            repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.LastHeartbeat != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task RunCleanupOnceAsync_DoesNotQueryTimeoutAt_WhenNoExecutionRequiresCleanup()
    {
        // Arrange
        _executions
            .Setup(repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());

        // Act
        await _subject.RunCleanupOnceAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.TimeoutAt != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Never);
        _executions.Verify(
            repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.LastHeartbeat != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_options.StaleJobCleanupInterval, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(_options.TimedOutJobReapInterval, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
    }

    [TestCase(3)]
    [TestCase(11)]
    public async Task RunTimedOutJobReapOnceAsync_ReapsOnlyPastTheGrace_WhenTheStaleCutoffIsConfigured(
        int graceMinutes)
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        _options.RunningStaleCutoff = TimeSpan.FromMinutes(graceMinutes);
        var justExpired = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            TimeoutAt = time.GetUtcNow().UtcDateTime.AddSeconds(-1)
        };
        var pastGrace = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            TimeoutAt = time.GetUtcNow().UtcDateTime - _options.RunningStaleCutoff - TimeSpan.FromSeconds(1)
        };
        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.TimeoutAt != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobExecutionQuery query, PagingParameter _, SortParameter? _, CancellationToken _) =>
                new PagedList<JobExecution>
                {
                    Items = new[] { justExpired, pastGrace }
                        .Where(execution => execution.TimeoutAt <= query.TimeoutAt!.Max)
                        .ToList()
                });
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
            _runtime.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            time,
            _migrationState);

        // Act
        await subject.RunTimedOutJobReapOnceAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _executions.Verify(
                repository => repository.TryMarkRunningFailedAsync(
                    pastGrace.Id,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.TryMarkRunningFailedAsync(
                    justExpired.Id,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ReclaimOwnOrphanedRunningAsync_CancelsTheRequestedRow_WhenTheOrphanIsAlsoOwned()
    {
        // Arrange
        var running = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId
        };
        var cancelling = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.CancellationRequested,
            NodeId = LocalNodeId
        };
        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.NodeId == LocalNodeId && query.Statuses != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [running, cancelling], TotalItems = 2 });
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _executions
            .Setup(repository => repository.TryMarkCancellationRequestedCancelledAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.ReclaimOwnOrphanedRunningAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _executions.Verify(
                repository => repository.TryMarkCancellationRequestedCancelledAsync(
                    cancelling.Id,
                    "Node node-A restarted while this execution awaited cancellation",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.TryMarkRunningFailedAsync(
                    running.Id,
                    "Node node-A restarted while this execution was Running",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.TryMarkRunningFailedAsync(
                    cancelling.Id,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _executions.Verify(
                repository => repository.TryMarkCancellationRequestedCancelledAsync(
                    running.Id,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task TryAdmitAsync_ClaimsTheScheduledType_WhenAnotherTypeFillsTheCandidatePage()
    {
        // Arrange
        const string nodeId = "node-a";
        var minority = new JobClaimCandidate(Guid.NewGuid(), "minority", null);
        var candidates = Enumerable.Range(0, 499)
            .Select(_ => new JobClaimCandidate(Guid.NewGuid(), "majority", null))
            .Append(minority)
            .ToList();
        var executions = new Mock<IJobExecutionRepository> { CallBase = true };
        executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 500),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);
        executions
            .Setup(repository => repository.TryClaimAsync(minority.Id, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecution
            {
                Id = minority.Id,
                JobType = minority.JobType,
                Scope = JobScope.System,
                Status = JobExecutionStatus.Running
            });

        var nodeStatuses = new Mock<IExtensionNodeStatusRepository> { CallBase = true };
        nodeStatuses
            .Setup(repository => repository.GetByNodeIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtensionNodeStatus?)null);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionNodeStatusRepository)))
            .Returns(nodeStatuses.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(executions.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(scope => scope.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(serviceScope.Object);

        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions { PoolSize = 5 },
            TimeProvider.System,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        scheduler.TrySelectAdmission(
            [
                new JobClaimCandidate(Guid.NewGuid(), "majority", null),
                new JobClaimCandidate(Guid.NewGuid(), "minority", null)
            ],
            new HashSet<Guid>(),
            out var initial);
        scheduler.CommitAdmission(initial);
        scheduler.Release(initial.Id);

        var hostEnvironment = new MagpieHostEnvironment(
            nodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "majority",
                    DisplayName = "Majority",
                    Category = "Test",
                    Scope = JobScope.System
                },
                new JobSpec
                {
                    JobType = "minority",
                    DisplayName = "Minority",
                    Category = "Test",
                    Scope = JobScope.System
                }
            }),
            Mock.Of<IJobRuntime>(),
            scheduler,
            new JobRuntimeOptions(),
            hostEnvironment,
            scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);

        // Act
        var admitted = await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admitted, Is.True);
            executions.Verify(
                repository => repository.TryClaimAsync(minority.Id, nodeId, It.IsAny<CancellationToken>()),
                Times.Once);
            executions.Verify(
                repository => repository.GetClaimableCandidatesAsync(
                    It.Is<PagingParameter>(paging => paging.PageNumber == 2),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task TryAdmitAsync_ReadsOneCandidatePage_WhenThePageIsFull()
    {
        // Arrange
        const string nodeId = "node-a";
        var minority = new JobClaimCandidate(Guid.NewGuid(), "minority", null);
        var firstPage = Enumerable.Range(0, 500)
            .Select(_ => new JobClaimCandidate(Guid.NewGuid(), "majority", null))
            .ToList();
        var secondPage = Enumerable.Range(0, 499)
            .Select(_ => new JobClaimCandidate(Guid.NewGuid(), "majority", null))
            .Append(minority)
            .ToList();
        var executions = new Mock<IJobExecutionRepository> { CallBase = true };
        executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 500),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage);
        executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 500),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondPage);
        executions
            .Setup(repository => repository.TryClaimAsync(It.IsAny<Guid>(), nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid executionId, string _, CancellationToken _) => new JobExecution
            {
                Id = executionId,
                JobType = "majority",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Running
            });

        var nodeStatuses = new Mock<IExtensionNodeStatusRepository> { CallBase = true };
        nodeStatuses
            .Setup(repository => repository.GetByNodeIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtensionNodeStatus?)null);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionNodeStatusRepository)))
            .Returns(nodeStatuses.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(executions.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(scope => scope.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(serviceScope.Object);

        var runtime = new Mock<IJobRuntime>();
        runtime
            .Setup(jobRuntime => jobRuntime.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);

        var hostEnvironment = new MagpieHostEnvironment(
            nodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "majority",
                    DisplayName = "Majority",
                    Category = "Test",
                    Scope = JobScope.System
                },
                new JobSpec
                {
                    JobType = "minority",
                    DisplayName = "Minority",
                    Category = "Test",
                    Scope = JobScope.System
                }
            }),
            runtime.Object,
            new MlfqJobConcurrencyScheduler(
                new JobRuntimeOptions { PoolSize = 50 },
                TimeProvider.System,
                NullLogger<MlfqJobConcurrencyScheduler>.Instance),
            new JobRuntimeOptions(),
            hostEnvironment,
            scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);

        // Act
        var admitted = await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admitted, Is.True);
            executions.Verify(
                repository => repository.GetClaimableCandidatesAsync(
                    It.IsAny<PagingParameter>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            executions.Verify(
                repository => repository.TryClaimAsync(minority.Id, nodeId, It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task TryAdmitAsync_LeavesTheExecutionQueued_WhenTheJobTypeIsNotRegisteredOnThisNode()
    {
        // Arrange
        const string nodeId = "node-a";
        var unregistered = new JobClaimCandidate(Guid.NewGuid(), "other-node-type", null);
        var executions = new Mock<IJobExecutionRepository> { CallBase = true };
        executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([unregistered]);

        var nodeStatuses = new Mock<IExtensionNodeStatusRepository> { CallBase = true };
        nodeStatuses
            .Setup(repository => repository.GetByNodeIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtensionNodeStatus?)null);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionNodeStatusRepository)))
            .Returns(nodeStatuses.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(executions.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(scope => scope.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(serviceScope.Object);

        var hostEnvironment = new MagpieHostEnvironment(
            nodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[]
            {
                new JobSpec
                {
                    JobType = "local-type",
                    DisplayName = "Local",
                    Category = "Test",
                    Scope = JobScope.System
                }
            }),
            Mock.Of<IJobRuntime>(),
            new MlfqJobConcurrencyScheduler(
                new JobRuntimeOptions { PoolSize = 5 },
                TimeProvider.System,
                NullLogger<MlfqJobConcurrencyScheduler>.Instance),
            new JobRuntimeOptions(),
            hostEnvironment,
            scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            TimeProvider.System,
            _migrationState);

        // Act
        var admitted = await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(admitted, Is.False);
        executions.Verify(
            repository => repository.TryClaimAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        executions.Verify(
            repository => repository.TryMarkQueuedFailedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task DispatchCancellationAsync_CallsRuntime_WhenExecutionOwnedByThisNode()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        // Act
        await _subject.DispatchCancellationAsync(executionId, LocalNodeId, default).ConfigureAwait(false);

        // Assert
        _runtime.Verify(r => r.TryCancelAsync(executionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DispatchCancellationAsync_SkipsRuntime_WhenOwnerTargetsAnotherNode()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        // Act
        await _subject.DispatchCancellationAsync(executionId, "node-B", default).ConfigureAwait(false);

        // Assert
        _runtime.Verify(r => r.TryCancelAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
