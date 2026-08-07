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
    private const string LocalNodeId = "node-A";

    private Mock<IAuditRecordRepository> _audits = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private Mock<IJobManager> _jobManager = null!;
    private JobRuntimeOptions _options = null!;
    private Mock<IJobRetryPolicy> _retryPolicy = null!;
    private Mock<IJobRuntime> _runtime = null!;
    private JobRuntimeBackgroundService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _audits = new Mock<IAuditRecordRepository>();
        _executions = new Mock<IJobExecutionRepository>();
        _jobManager = new Mock<IJobManager>();
        _retryPolicy = new Mock<IJobRetryPolicy>();
        _runtime = new Mock<IJobRuntime>();
        _options = new JobRuntimeOptions
        {
            StaleJobCleanupInterval = TimeSpan.FromHours(1),
            TimedOutJobReapInterval = TimeSpan.FromSeconds(30)
        };

        var serviceProvider = new Mock<IServiceProvider>();
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
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));
        serviceProvider
            .Setup(provider => provider.GetService(typeof(ITransactionManager)))
            .Returns(transactionManager.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(scope => scope.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(serviceScope.Object);

        var hostEnvironment = new MagpieHostEnvironment(
            LocalNodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);

        var spec = new JobSpec
        {
            JobType = "ReleaseSearchItem",
            DisplayName = "Release search item",
            Category = "Test",
            Scope = JobScope.System,
            FailurePolicy = JobFailurePolicy.Requeue
        };

        _subject = new JobRuntimeBackgroundService(
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == new[] { spec }),
            _runtime.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            _options,
            hostEnvironment,
            scopeFactory.Object,
            Mock.Of<IEventBus>(),
            NullLogger<JobRuntimeBackgroundService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _subject.Dispose();
    }

    [Test]
    public void JobRuntimeOptions_Defaults_DecoupleTimedOutReapFromHourlyStaleCleanup()
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
                It.IsAny<bool>(),
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
    public async Task StartAsync_RepeatsTimedOutQuery_OnDedicatedIntervalBeforeStaleCleanupRepeats()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 25, 5, 0, 0, TimeSpan.Zero));
        var firstReap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reapCount = 0;
        var executions = new Mock<IJobExecutionRepository>();
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

        var scheduleTriggers = new Mock<IScheduleTriggerRepository>();
        scheduleTriggers
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ScheduleTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ScheduleTrigger>());

        var extensionNodes = new Mock<IExtensionNodeStatusRepository>();
        extensionNodes
            .Setup(repository => repository.GetByNodeIdAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtensionNodeStatus?)null);
        var users = new Mock<IUserRepository>();
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
            NullLogger<JobRuntimeBackgroundService>.Instance,
            timeProvider);

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

    [Test]
    public async Task TryAdmitAsync_UsesAllCandidatePages_WhenSchedulerSelectsLaterType()
    {
        // Arrange
        const string nodeId = "node-a";
        var majority = Enumerable.Range(0, 500)
            .Select(_ => new JobClaimCandidate(Guid.NewGuid(), "majority", null))
            .ToList();
        var minority = new JobClaimCandidate(Guid.NewGuid(), "minority", null);
        var executions = new Mock<IJobExecutionRepository>();
        executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 500),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(majority);
        executions
            .Setup(repository => repository.GetClaimableCandidatesAsync(
                It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 500),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([minority]);
        executions
            .Setup(repository => repository.TryClaimAsync(minority.Id, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecution
            {
                Id = minority.Id,
                JobType = minority.JobType,
                Scope = JobScope.System,
                Status = JobExecutionStatus.Running
            });

        var nodeStatuses = new Mock<IExtensionNodeStatusRepository>();
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
            new JobRuntimeOptions { MaxConcurrentJobs = 5 },
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
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == Array.Empty<JobSpec>()),
            Mock.Of<IJobRuntime>(),
            scheduler,
            new JobRuntimeOptions(),
            hostEnvironment,
            scopeFactory.Object,
            Mock.Of<IEventBus>(),
            NullLogger<JobRuntimeBackgroundService>.Instance);

        // Act
        var admitted = await subject.TryAdmitAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(admitted, Is.True);
        executions.Verify(
            repository => repository.TryClaimAsync(minority.Id, nodeId, It.IsAny<CancellationToken>()),
            Times.Once);
        executions.Verify(
            repository => repository.GetClaimableCandidatesAsync(
                It.Is<PagingParameter>(paging => paging.PageNumber == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
