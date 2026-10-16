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
using Microsoft.Extensions.Hosting;
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
    private Mock<IJobRuntime> _runtime = null!;
    private Mock<IScheduleTriggerRepository> _scheduleTriggers = null!;
    private Mock<IServiceScopeFactory> _scopeFactory = null!;
    private Mock<ISettingsRepository> _settings = null!;
    private MagpieHostEnvironment _hostEnvironment = null!;
    private JobSpec _spec = null!;
    private JobShutdownSignal _shutdownSignal = null!;
    private JobRuntimeBackgroundService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _migrationState = new DatabaseMigrationState();
        _migrationState.Complete();
        _migrationState.RecordSchemaCheck([]);
        _audits = new Mock<IAuditRecordRepository> { CallBase = true };
        _executions = new Mock<IJobExecutionRepository> { CallBase = true };
        _executions.Setup(repository => repository.QueryAsync(It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        _jobManager = new Mock<IJobManager>();
        _runtime = new Mock<IJobRuntime>();
        _scheduleTriggers = new Mock<IScheduleTriggerRepository> { CallBase = true };
        _settings = new Mock<ISettingsRepository>();
        _options = new JobRuntimeOptions
        {
            StaleJobCleanupInterval = TimeSpan.FromHours(1),
            ClusteredStaleJobCleanupInterval = TimeSpan.FromSeconds(30)
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
            .Setup(provider => provider.GetService(typeof(DefaultJobRetryPolicy)))
            .Returns(new DefaultJobRetryPolicy(
                _settings.Object,
                new FakeTimeProvider(),
                NullLogger<DefaultJobRetryPolicy>.Instance));
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

        _shutdownSignal = new JobShutdownSignal(Mock.Of<IHostApplicationLifetime>());

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

    [Test]
    public async Task RunOnceAsync_ReadsNoTriggers_WhenTheDatabaseHasUnknownMigrations()
    {
        // Arrange
        _migrationState.RecordSchemaCheck(["20991231000000_Future"]);

        // Act
        await _subject.RunOnceAsync(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), default)
            .ConfigureAwait(false);

        // Assert
        _scopeFactory.Verify(factory => factory.CreateScope(), Times.Never);
    }

    [Test]
    public async Task RunCleanupOnceAsync_ReadsNoExecutions_WhenTheDatabaseHasUnknownMigrations()
    {
        // Arrange
        _migrationState.RecordSchemaCheck(["20991231000000_Future"]);

        // Act
        await _subject.RunCleanupOnceAsync(default).ConfigureAwait(false);

        // Assert
        _scopeFactory.Verify(factory => factory.CreateScope(), Times.Never);
    }

    [Test]
    public async Task ReclaimOwnOrphanedRunningAsync_ReadsNoExecutions_WhenTheDatabaseHasUnknownMigrations()
    {
        // Arrange
        _migrationState.RecordSchemaCheck(["20991231000000_Future"]);

        // Act
        await _subject.ReclaimOwnOrphanedRunningAsync(default).ConfigureAwait(false);

        // Assert
        _scopeFactory.Verify(factory => factory.CreateScope(), Times.Never);
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
    public void JobRuntimeOptions_UsesSeparateReapIntervals_WhenConstructed()
    {
        // Act
        var options = new JobRuntimeOptions();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.ClusteredStaleJobCleanupInterval, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.StaleJobCleanupInterval, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(options.ClusteredStaleJobCleanupInterval, Is.LessThan(options.StaleJobCleanupInterval));
        }
    }

    [Test]
    public async Task RunCleanupOnceAsync_DoesNotRetry_WhenCancellationWinsTransitionRace()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var timedOut = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30)
        };

        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.LastHeartbeat != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [timedOut], TotalItems = 1 });
        _executions
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.Is<JobExecution>(execution => execution.Id == executionId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _subject.RunCleanupOnceAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RunCleanupOnceAsync_EnqueuesRetry_WhenStoppingBeginsAfterReclaim()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var timedOut = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30)
        };
        using var stopping = new CancellationTokenSource();

        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.LastHeartbeat != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [timedOut], TotalItems = 1 });
        _executions
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.Is<JobExecution>(execution => execution.Id == executionId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => stopping.Cancel())
            .ReturnsAsync(true);
        _settings
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<SettingsGroupLookupKey>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroupLookupKey _, CancellationToken cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            });

        // Act
        await _subject.RunCleanupOnceAsync(stopping.Token).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.CreateAsync(
                It.Is<JobExecution>(requeue => requeue.OriginalExecutionId == executionId),
                null,
                It.Is<CancellationToken>(token => !token.IsCancellationRequested)),
            Times.Once);
    }

    [Test]
    public async Task CleanupPasses_EnqueueOneRetry_WhenStaleReclaimPassesRace()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var candidate = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            LastHeartbeat = DateTime.UtcNow.AddHours(-2)
        };
        var transitionCount = 0;

        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running &&
                    query.LastHeartbeat != null),
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
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.Is<JobExecution>(execution => execution.Id == executionId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref transitionCount) == 1);

        // Act
        await Task.WhenAll(
            _subject.RunCleanupOnceAsync(default),
            _subject.RunCleanupOnceAsync(default)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitionCount, Is.EqualTo(2));
            _executions.Verify(
                repository => repository.CreateAsync(
                    It.Is<JobExecution>(requeue => requeue.OriginalExecutionId == executionId),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task RunCleanupOnceAsync_QueriesOnlyHeartbeatAge_WhenNoExecutionRequiresCleanup()
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
                It.Is<JobExecutionQuery>(query => query.ExecutionTimestamp != null),
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
            Assert.That(_options.ClusteredStaleJobCleanupInterval, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
    }

    [TestCase(3)]
    [TestCase(11)]
    public async Task RunCleanupOnceAsync_ReapsOnlyPastTheGrace_WhenTheStaleCutoffIsConfigured(
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
            LastHeartbeat = time.GetUtcNow().UtcDateTime.AddSeconds(-1)
        };
        var pastGrace = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            LastHeartbeat = time.GetUtcNow().UtcDateTime - _options.RunningStaleCutoff - TimeSpan.FromSeconds(1)
        };
        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.LastHeartbeat != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobExecutionQuery query, PagingParameter _, SortParameter? _, CancellationToken _) =>
                new PagedList<JobExecution>
                {
                    Items = new[] { justExpired, pastGrace }
                        .Where(execution => execution.LastHeartbeat <= query.LastHeartbeat!.Max)
                        .ToList()
                });
        _executions
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.IsAny<JobExecution>(),
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
        await subject.RunCleanupOnceAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _executions.Verify(
                repository => repository.TryRecoverInactiveAsync(
                    It.Is<JobExecution>(execution => execution.Id == pastGrace.Id),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.TryRecoverInactiveAsync(
                    It.Is<JobExecution>(execution => execution.Id == justExpired.Id),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task StartAsync_ReclaimsAnotherNodesRun_WhenItsHeartbeatTurnsStaleOneCutoffAfterTheStart()
    {
        // Arrange
        var time = new Mock<FakeTimeProvider>(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero))
        {
            CallBase = true
        };
        var cleanupSleeps = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        time
            .Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                TimeSpan.FromMinutes(3),
                It.IsAny<TimeSpan>()))
            .CallBase()
            .Callback(() => cleanupSleeps.TrySetResult());
        _options.RunningStaleCutoff = TimeSpan.FromMinutes(3);
        var orphan = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = "node-before-restart",
            LastHeartbeat = new DateTime(2026, 1, 1, 11, 59, 30, DateTimeKind.Utc)
        };
        var reclaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running &&
                    query.LastHeartbeat!.Max == new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [orphan] });
        _executions
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => reclaimed.TrySetResult())
            .ReturnsAsync(true);
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
            _runtime.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            new Mock<IEventBus> { DefaultValue = DefaultValue.Mock }.Object,
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            time.Object,
            _migrationState);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await cleanupSleeps.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        time.Object.Advance(TimeSpan.FromMinutes(3));
        await reclaimed.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.TryRecoverInactiveAsync(
                It.Is<JobExecution>(execution => execution.Id == orphan.Id),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.ReclaimOwnOrphanedRunningAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _executions.Verify(
                repository => repository.TryRecoverInactiveAsync(
                    It.Is<JobExecution>(execution => execution.Id == cancelling.Id),
                    "Node node-A restarted while this execution awaited cancellation",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.TryRecoverInactiveAsync(
                    It.Is<JobExecution>(execution => execution.Id == running.Id),
                    "Node node-A restarted while this execution was Running",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.CreateAsync(
                    It.Is<JobExecution>(requeue => requeue.OriginalExecutionId == running.Id),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.CreateAsync(
                    It.Is<JobExecution>(requeue => requeue.OriginalExecutionId == cancelling.Id),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task RunCleanupOnceAsync_RequeuesAsTheNextAttemptWithoutBackoff_WhenItReclaimsAStaleImmediateRun()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var stale = new JobExecution
        {
            Id = executionId,
            JobType = "ReleaseSearchItem",
            ExecutionMode = JobExecutionMode.Immediate,
            Status = JobExecutionStatus.Running,
            NodeId = "node-B",
            Attempt = 2,
            LastHeartbeat = DateTime.UtcNow.AddHours(-2)
        };
        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Running && query.LastHeartbeat != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [stale], TotalItems = 1 });
        _executions
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.Is<JobExecution>(execution => execution.Id == executionId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.RunCleanupOnceAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.CreateAsync(
                It.Is<JobExecution>(requeue =>
                    requeue.OriginalExecutionId == executionId &&
                    requeue.Status == JobExecutionStatus.Queued &&
                    requeue.ExecutionMode == JobExecutionMode.Queue &&
                    requeue.Attempt == 3 &&
                    requeue.NotBeforeAt == null),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ReclaimOwnOrphanedRunningAsync_EndsFailedWithoutRequeue_WhenTheReclaimPassesTheAttemptLimit()
    {
        // Arrange
        var orphan = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Running,
            NodeId = LocalNodeId,
            Attempt = 5
        };
        _executions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.NodeId == LocalNodeId && query.Statuses != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [orphan], TotalItems = 1 });
        _executions
            .Setup(repository => repository.TryRecoverInactiveAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.ReclaimOwnOrphanedRunningAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _executions.Verify(
                repository => repository.TryRecoverInactiveAsync(
                    It.Is<JobExecution>(execution => execution.Id == orphan.Id),
                    "Node node-A restarted while this execution was Running",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<JobExecution>(),
                    It.IsAny<Guid?>(),
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
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
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
                Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
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
    public async Task TryAdmitAsync_ClaimsNothing_WhenTheHostIsStopping()
    {
        // Arrange
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
            _runtime.Object,
            Mock.Of<IJobConcurrencyScheduler>(scheduler => scheduler.HasAdmissionCapacity()),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            TimeProvider.System,
            _migrationState);
        await _shutdownSignal.Cancellation.CancelAsync().ConfigureAwait(false);

        // Act
        var admitted = await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admitted, Is.False);
            _scopeFactory.Verify(factory => factory.CreateScope(), Times.Never);
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
                Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
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

    [TestCase("ReleaseSearchItem", 1)]
    [TestCase("MediaInfoExtractItem", 0)]
    public async Task TryAdmitAsync_ClaimsOnlyTheListedJobTypes_WhenTheNodeHasAJobList(
        string listedJobType,
        int expectedClaims)
    {
        // Arrange
        _executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d01"), "ReleaseSearchItem", null)
            ]);
        _executions
            .Setup(repository => repository.TryClaimAsync(It.IsAny<Guid>(), LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid executionId, string _, CancellationToken _) => new JobExecution
            {
                Id = executionId,
                JobType = "ReleaseSearchItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Running
            });
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
            _runtime.Object,
            new MlfqJobConcurrencyScheduler(
                new JobRuntimeOptions { PoolSize = 5 },
                TimeProvider.System,
                Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { _spec }),
                NullLogger<MlfqJobConcurrencyScheduler>.Instance),
            new JobRuntimeOptions { JobTypes = [listedJobType] },
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            TimeProvider.System,
            _migrationState);

        // Act
        await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.TryClaimAsync(It.IsAny<Guid>(), LocalNodeId, It.IsAny<CancellationToken>()),
            Times.Exactly(expectedClaims));
    }

    [TestCase(1, 1)]
    [TestCase(2, 2)]
    public async Task TryAdmitAsync_ClaimsMaxExecutions_WhenTheJobTypeHasANodeConcurrencyLimit(
        int max,
        int expectedClaims)
    {
        // Arrange
        _executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d01"), "ReleaseSearchItem", null),
                new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d02"), "ReleaseSearchItem", null),
                new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d03"), "ReleaseSearchItem", null)
            ]);
        _executions
            .Setup(repository => repository.TryClaimAsync(It.IsAny<Guid>(), LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid executionId, string _, CancellationToken _) => new JobExecution
            {
                Id = executionId,
                JobType = "ReleaseSearchItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Running
            });
        _runtime
            .Setup(runtime => runtime.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);
        var spec = _spec with { ConcurrencyLimit = new() { Max = max, PerNode = true } };
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            _runtime.Object,
            new MlfqJobConcurrencyScheduler(
                new JobRuntimeOptions { PoolSize = 50 },
                TimeProvider.System,
                Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
                NullLogger<MlfqJobConcurrencyScheduler>.Instance),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            TimeProvider.System,
            _migrationState);

        // Act
        await subject.TryAdmitAsync(default).ConfigureAwait(false);
        await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.TryClaimAsync(It.IsAny<Guid>(), LocalNodeId, It.IsAny<CancellationToken>()),
            Times.Exactly(expectedClaims));
    }

    [Test]
    public async Task TryAdmitAsync_ClaimsTheNextExecution_WhenTheRunningExecutionOfALimitedJobTypeEnds()
    {
        // Arrange
        var first = new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d01"), "ReleaseSearchItem", null);
        var second = new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d02"), "ReleaseSearchItem", null);
        _executions
            .SetupSequence(repository => repository.GetClaimableCandidatesAsync(
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second])
            .ReturnsAsync([second]);
        _executions
            .Setup(repository => repository.TryClaimAsync(It.IsAny<Guid>(), LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid executionId, string _, CancellationToken _) => new JobExecution
            {
                Id = executionId,
                JobType = "ReleaseSearchItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Running
            });
        var running = new TaskCompletionSource();
        _runtime
            .Setup(runtime => runtime.ExecuteAsync(first.Id, It.IsAny<CancellationToken>()))
            .Returns(running.Task);
        var spec = _spec with { ConcurrencyLimit = new() { Max = 1, PerNode = true } };
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var mlfq = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions { PoolSize = 50 },
            time,
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new Mock<IJobConcurrencyScheduler>();
        scheduler.Setup(s => s.HasAdmissionCapacity()).Returns(mlfq.HasAdmissionCapacity);
        scheduler
            .Setup(s => s.TrySelectAdmission(
                It.IsAny<IReadOnlyList<JobClaimCandidate>>(),
                It.IsAny<ISet<Guid>>(),
                out It.Ref<JobClaimCandidate>.IsAny))
            .Returns(mlfq.TrySelectAdmission);
        scheduler
            .Setup(s => s.CommitAdmission(It.IsAny<JobClaimCandidate>()))
            .Callback<JobClaimCandidate>(mlfq.CommitAdmission);
        scheduler
            .Setup(s => s.Release(first.Id))
            .Callback((Guid id) =>
            {
                mlfq.Release(id);
                released.SetResult();
            });
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            _runtime.Object,
            scheduler.Object,
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            time,
            _migrationState);
        await subject.TryAdmitAsync(default).ConfigureAwait(false);
        running.SetResult();
        await released.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        // Act
        await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.TryClaimAsync(second.Id, LocalNodeId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryAdmitAsync_TriesOneClaimOfTheJobType_WhenItsClusterConcurrencyLimitRefusesTheClaim()
    {
        // Arrange
        _executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d01"), "ReleaseSearchItem", null),
                new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d02"), "ReleaseSearchItem", null),
                new JobClaimCandidate(new Guid("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d03"), "ReleaseSearchItem", null)
            ]);
        var spec = _spec with { ConcurrencyLimit = new() { Max = 1, PerNode = false } };
        using var subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            _runtime.Object,
            new MlfqJobConcurrencyScheduler(
                new JobRuntimeOptions { PoolSize = 50 },
                TimeProvider.System,
                Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
                NullLogger<MlfqJobConcurrencyScheduler>.Instance),
            _options,
            _hostEnvironment,
            _scopeFactory.Object,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            NullLogger<JobRuntimeBackgroundService>.Instance,
            TimeProvider.System,
            _migrationState);

        // Act
        var admitted = await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admitted, Is.False);
            _executions.Verify(
                repository => repository.TryClaimAsync(It.IsAny<Guid>(), LocalNodeId, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
