using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class DefaultJobRuntimeTests
{
    private const string JobType = "test-job";
    private const string NodeId = "test-node";

    private Mock<IAuditRecordRepository> _audits = null!;
    private JobExecution _execution = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private Mock<IJob> _handler = null!;
    private TaskCompletionSource _handlerEntered = null!;
    private TaskCompletionSource _handlerRelease = null!;
    private Mock<IJobExecutionScope> _jobScope = null!;
    private Mock<IJobRegistry> _registry = null!;
    private JobShutdownSignal _shutdownSignal = null!;
    private JobSpec _spec = null!;
    private IJobRuntime _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _shutdownSignal = new JobShutdownSignal();
        _execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = JobType,
            Status = JobExecutionStatus.Queued
        };
        _handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handlerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _executions = new Mock<IJobExecutionRepository>();
        _executions
            .Setup(repository => repository.TryStartAsync(_execution.Id, NodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _executions
            .Setup(repository => repository.TrySetRunningTimeoutAsync(
                _execution.Id,
                NodeId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _executions
            .Setup(repository => repository.TouchHeartbeatAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _executions
            .Setup(repository => repository.TryMarkRunningCompletedAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _executions
            .Setup(repository => repository.TryMarkCancellationRequestedCancelledAsync(
                _execution.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _audits = new Mock<IAuditRecordRepository>();
        _audits
            .Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditRecord record, CancellationToken _) => record);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(_executions.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobManager)))
            .Returns(Mock.Of<IJobManager>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobRetryPolicy)))
            .Returns(Mock.Of<IJobRetryPolicy>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IAuditRecordRepository)))
            .Returns(_audits.Object);
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

        _spec = new JobSpec
        {
            JobType = JobType,
            DisplayName = JobType,
            Category = "Test",
            Scope = JobScope.System,
            OnDemand = true,
            FailurePolicy = JobFailurePolicy.Fail
        };
        var spec = _spec;
        _registry = new Mock<IJobRegistry>();
        _registry
            .Setup(jobRegistry => jobRegistry.TryGetJobSpec(JobType, out spec))
            .Returns(true);

        _handler = new Mock<IJob>();
        _jobScope = new Mock<IJobExecutionScope>();
        var handler = _handler.Object;
        _jobScope
            .Setup(scope => scope.TryGetHandler(spec, out handler))
            .Returns(true);
        var jobScopes = new Mock<IJobExecutionScopeFactory>();
        jobScopes.Setup(factory => factory.CreateScope(_execution.Id)).Returns(_jobScope.Object);

        var hostEnvironment = new MagpieHostEnvironment(
            NodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);

        _subject = new DefaultJobRuntime(
            jobScopes.Object,
            scopeFactory.Object,
            _registry.Object,
            hostEnvironment,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            TimeProvider.System,
            NullLogger<DefaultJobRuntime>.Instance,
            TimeSpan.FromMilliseconds(25));
    }

    [TearDown]
    public void TearDown()
    {
        _handlerRelease.TrySetResult();
        _subject.Dispose();
    }

    [Test]
    public async Task TryCancelAsync_DoesNotCancelLocalHandler_WhenTimeoutWonTransitionRace()
    {
        // Arrange
        var handlerWasCancelled = false;
        var failed = new JobExecution
        {
            Id = _execution.Id,
            JobType = JobType,
            Status = JobExecutionStatus.Failed
        };
        var running = _execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_execution)
            .ReturnsAsync(running)
            .ReturnsAsync(failed);
        _executions
            .Setup(repository => repository.RequestCancellationAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCancellationRequestResult?)null);
        _handler
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContext _, CancellationToken cancellationToken) =>
            {
                _handlerEntered.TrySetResult();
                try
                {
                    await _handlerRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    handlerWasCancelled = true;
                    throw;
                }
            });
        var run = _subject.ExecuteAsync(_execution.Id);
        await _handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Act
        var cancellationRequested = await _subject.TryCancelAsync(_execution.Id).ConfigureAwait(false);
        _handlerRelease.TrySetResult();
        await run.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancellationRequested, Is.False);
            Assert.That(handlerWasCancelled, Is.False);
            _executions.Verify(
                repository => repository.TryMarkCancellationRequestedCancelledAsync(
                    _execution.Id,
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _audits.Verify(
                repository => repository.CreateAsync(
                    It.Is<AuditRecord>(record => record.Operation == "JobCancelled"),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_MarksHostShutdown_WhenTheShutdownSignalCancelsARunWithNoCallerToken()
    {
        // Arrange
        var running = _execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_execution)
            .ReturnsAsync(running);
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                _execution.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContext _, CancellationToken cancellationToken) =>
            {
                _handlerEntered.TrySetResult();
                await _handlerRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            });
        var run = _subject.ExecuteAsync(_execution.Id);
        await _handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var outstandingWhileRunning = _shutdownSignal.Outstanding.Count;

        // Act
        await _shutdownSignal.Cancellation.CancelAsync().ConfigureAwait(false);
        await run.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outstandingWhileRunning, Is.EqualTo(1));
            Assert.That(_shutdownSignal.Outstanding, Has.Count.EqualTo(0));
            _executions.Verify(
                repository => repository.TryMarkRunningFailedAsync(
                    _execution.Id,
                    "Interrupted by host shutdown",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _audits.Verify(
                repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_MarksCancelled_WhenCancelledHandlerThrowsANonCancellationError()
    {
        // Arrange
        var running = _execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_execution)
            .ReturnsAsync(running);
        _executions
            .Setup(repository => repository.RequestCancellationAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCancellationRequestResult(NodeId, CancelledBeforeStart: false));
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                _execution.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _executions
            .Setup(repository => repository.TryMarkCancellationRequestedCancelledAsync(
                _execution.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContext _, CancellationToken cancellationToken) =>
            {
                _handlerEntered.TrySetResult();
                try
                {
                    await _handlerRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException("Operation cancelled by user");
                }
            });
        var run = _subject.ExecuteAsync(_execution.Id);
        await _handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Act
        await _subject.TryCancelAsync(_execution.Id).ConfigureAwait(false);
        await run.ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.TryMarkCancellationRequestedCancelledAsync(
                _execution.Id,
                "Operation cancelled by user",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_MarksCancelled_WhenCancelledHandlerReturnsNormally()
    {
        // Arrange
        var running = _execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_execution)
            .ReturnsAsync(running);
        _executions
            .Setup(repository => repository.RequestCancellationAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCancellationRequestResult(NodeId, CancelledBeforeStart: false));
        _executions
            .Setup(repository => repository.TryMarkCancellationRequestedCancelledAsync(
                _execution.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContext _, CancellationToken cancellationToken) =>
            {
                _handlerEntered.TrySetResult();
                try
                {
                    await _handlerRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            });
        var run = _subject.ExecuteAsync(_execution.Id);
        await _handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Act
        var cancellationRequested = await _subject.TryCancelAsync(_execution.Id).ConfigureAwait(false);
        await run.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancellationRequested, Is.True);
            _executions.Verify(
                repository => repository.TryMarkCancellationRequestedCancelledAsync(
                    _execution.Id,
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _audits.Verify(
                repository => repository.CreateAsync(
                    It.Is<AuditRecord>(record => record.Operation == "JobCancelled"),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_CancelsHandler_WhenCancellationWinsBeforeLocalRegistration()
    {
        // Arrange
        var timeoutRegistrationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutRegistrationRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerStartedCancelled = false;
        var cancellationRequestedState = new JobExecution
        {
            Id = _execution.Id,
            JobType = JobType,
            Status = JobExecutionStatus.CancellationRequested
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_execution)
            .ReturnsAsync(cancellationRequestedState);
        _executions
            .Setup(repository => repository.TrySetRunningTimeoutAsync(
                _execution.Id,
                NodeId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                timeoutRegistrationEntered.TrySetResult();
                await timeoutRegistrationRelease.Task.ConfigureAwait(false);
                return true;
            });
        _executions
            .Setup(repository => repository.RequestCancellationAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCancellationRequestResult(NodeId, CancelledBeforeStart: false));
        _executions
            .Setup(repository => repository.TryMarkCancellationRequestedCancelledAsync(
                _execution.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns((JobContext _, CancellationToken cancellationToken) =>
            {
                handlerStartedCancelled = cancellationToken.IsCancellationRequested;
                return Task.CompletedTask;
            });
        var run = _subject.ExecuteAsync(_execution.Id);
        await timeoutRegistrationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Act
        var cancellationRequested = await _subject.TryCancelAsync(_execution.Id).ConfigureAwait(false);
        timeoutRegistrationRelease.TrySetResult();
        await run.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancellationRequested, Is.True);
            Assert.That(handlerStartedCancelled, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_WritesOneTimeoutAudit_WhenLocalRuntimeWinsTransition()
    {
        // Arrange
        _spec = _spec with { Timeout = TimeSpan.FromMilliseconds(25) };
        var spec = _spec;
        _registry
            .Setup(jobRegistry => jobRegistry.TryGetJobSpec(JobType, out spec))
            .Returns(true);
        var handler = _handler.Object;
        _jobScope
            .Setup(scope => scope.TryGetHandler(spec, out handler))
            .Returns(true);
        var running = _execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_execution)
            .ReturnsAsync(running);
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                _execution.Id,
                It.Is<string>(reason => reason.Contains("Timed out", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContext _, CancellationToken cancellationToken) =>
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false));

        // Act
        await _subject.ExecuteAsync(_execution.Id).ConfigureAwait(false);

        // Assert
        _audits.Verify(
            repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "JobTimeout"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_FinalizesOperatorCancellation_WhenHostShutdownIsAlsoSignalled()
    {
        // Arrange
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalStatus = JobExecutionStatus.CancellationRequested;
        var running = _execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(_execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_execution)
            .ReturnsAsync(running);
        _executions
            .SetupSequence(repository => repository.RequestCancellationAsync(
                _execution.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCancellationRequestResult(NodeId, CancelledBeforeStart: false))
            .ReturnsAsync((JobCancellationRequestResult?)null);
        _executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                _execution.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => terminalStatus = JobExecutionStatus.Failed)
            .ReturnsAsync(true);
        _executions
            .Setup(repository => repository.TryMarkCancellationRequestedCancelledAsync(
                _execution.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => terminalStatus = JobExecutionStatus.Cancelled)
            .ReturnsAsync(true);
        _handler
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContext _, CancellationToken cancellationToken) =>
            {
                _handlerEntered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    await cancellationRelease.Task.ConfigureAwait(false);
                    throw;
                }
            });
        using var shutdown = new CancellationTokenSource();
        var run = _subject.ExecuteAsync(_execution.Id, shutdown.Token);
        await _handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Act
        var cancellationRequested = await _subject.TryCancelAsync(_execution.Id).ConfigureAwait(false);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await shutdown.CancelAsync().ConfigureAwait(false);
        cancellationRelease.TrySetResult();
        await run.ConfigureAwait(false);
        var cancellationAfterCompletion = await _subject.TryCancelAsync(_execution.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancellationRequested, Is.True);
            Assert.That(cancellationAfterCompletion, Is.False);
            Assert.That(terminalStatus, Is.EqualTo(JobExecutionStatus.Cancelled));
            _executions.Verify(
                repository => repository.TryMarkRunningFailedAsync(
                    _execution.Id,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _executions.Verify(
                repository => repository.TryMarkCancellationRequestedCancelledAsync(
                    _execution.Id,
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_MarksCompletedWithoutCancelling_WhenLongRunningHandlerFinishes()
    {
        // Arrange
        const string jobType = "test-job";

        var executionId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = executionId,
            JobType = jobType,
            Status = JobExecutionStatus.Queued
        };
        var runningExecution = execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };

        var executions = new Mock<IJobExecutionRepository>();
        executions
            .SetupSequence(m => m.GetByIdAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution)
            .ReturnsAsync(runningExecution);
        executions
            .Setup(m => m.TryStartAsync(executionId, NodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        executions
            .Setup(m => m.TouchHeartbeatAsync(executionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        executions
            .Setup(m => m.TryMarkRunningCompletedAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var retryPolicy = new Mock<IJobRetryPolicy>();

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(executions.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobRetryPolicy)))
            .Returns(retryPolicy.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(Mock.Of<IJobManager>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IAuditRecordRepository)))
            .Returns(Mock.Of<IAuditRecordRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(ITransactionManager)))
            .Returns(Mock.Of<ITransactionManager>());
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);

        var handlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wasCancelled = false;

        var job = new Mock<IJob>();
        job
            .Setup(j => j.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobContext _, CancellationToken ct) =>
            {
                handlerEntered.TrySetResult(true);
                try
                {
                    await handlerRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                    throw;
                }
            });

        var spec = new JobSpec
        {
            JobType = jobType,
            DisplayName = jobType,
            Category = "Test",
            Scope = JobScope.System,
            OnDemand = true,
            FailurePolicy = JobFailurePolicy.Fail,
        };

        var registry = new Mock<IJobRegistry>();
        registry
            .Setup(r => r.TryGetJobSpec(jobType, out spec))
            .Returns(true);

        var jobScope = new Mock<IJobExecutionScope>();
        var handler = job.Object;
        jobScope
            .Setup(s => s.TryGetHandler(spec, out handler))
            .Returns(true);
        var jobScopes = new Mock<IJobExecutionScopeFactory>();
        jobScopes.Setup(f => f.CreateScope(executionId)).Returns(jobScope.Object);

        var hostEnvironment = new MagpieHostEnvironment(
            NodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);

        IJobRuntime subject = new DefaultJobRuntime(
            jobScopes.Object,
            scopeFactory.Object,
            registry.Object,
            hostEnvironment,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            TimeProvider.System,
            NullLogger<DefaultJobRuntime>.Instance,
            TimeSpan.FromMilliseconds(25));

        // Act
        var run = subject.ExecuteAsync(executionId);

        var entered = await Task.WhenAny(handlerEntered.Task, Task.Delay(TimeSpan.FromSeconds(5)))
            .ConfigureAwait(false);
        Assert.That(entered, Is.SameAs(handlerEntered.Task));

        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

        handlerRelease.TrySetResult(true);
        await run.ConfigureAwait(false);

        // Assert
        Assert.That(wasCancelled, Is.False);
        executions.Verify(m => m.TryMarkRunningCompletedAsync(executionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UsesBoundedRetry_WhenFailurePolicyRequeues()
    {
        // Arrange
        const string jobType = "test-job";

        var executionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = executionId,
            JobType = jobType,
            Status = JobExecutionStatus.Queued,
            CreatedByUserId = userId,
            Attempt = 1,
            LockPartition = "type:test-job"
        };
        var runningExecution = execution with
        {
            Status = JobExecutionStatus.Running,
            NodeId = NodeId
        };
        var retry = new JobExecution
        {
            JobType = jobType,
            Status = JobExecutionStatus.Queued,
            CreatedByUserId = userId,
            OriginalExecutionId = executionId,
            LockPartition = "type:test-job",
            Attempt = 2,
            NotBeforeAt = DateTime.UtcNow.AddSeconds(10)
        };

        var executions = new Mock<IJobExecutionRepository>();
        executions
            .SetupSequence(repository => repository.GetByIdAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution)
            .ReturnsAsync(runningExecution);
        executions
            .Setup(repository => repository.TryStartAsync(executionId, NodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        executions
            .Setup(repository => repository.TrySetRunningTimeoutAsync(
                executionId,
                NodeId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        executions
            .Setup(repository => repository.TouchHeartbeatAsync(executionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        executions
            .Setup(repository => repository.TryMarkRunningFailedAsync(
                executionId,
                "bad data",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        executions
            .Setup(repository => repository.CreateAsync(retry, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retry);

        var retryPolicy = new Mock<IJobRetryPolicy>();
        retryPolicy
            .Setup(policy => policy.BuildRetryAsync(execution, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retry);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(executions.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobRetryPolicy)))
            .Returns(retryPolicy.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobManager)))
            .Returns(Mock.Of<IJobManager>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IAuditRecordRepository)))
            .Returns(Mock.Of<IAuditRecordRepository>());
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

        var job = new Mock<IJob>();
        job
            .Setup(handler => handler.ExecuteAsync(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad data"));

        var spec = new JobSpec
        {
            JobType = jobType,
            DisplayName = jobType,
            Category = "Test",
            Scope = JobScope.System,
            OnDemand = true,
            FailurePolicy = JobFailurePolicy.Requeue,
        };

        var registry = new Mock<IJobRegistry>();
        registry
            .Setup(jobRegistry => jobRegistry.TryGetJobSpec(jobType, out spec))
            .Returns(true);

        var jobScope = new Mock<IJobExecutionScope>();
        var handler = job.Object;
        jobScope
            .Setup(scope => scope.TryGetHandler(spec, out handler))
            .Returns(true);
        var jobScopes = new Mock<IJobExecutionScopeFactory>();
        jobScopes.Setup(factory => factory.CreateScope(executionId)).Returns(jobScope.Object);

        var hostEnvironment = new MagpieHostEnvironment(
            NodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);

        IJobRuntime subject = new DefaultJobRuntime(
            jobScopes.Object,
            scopeFactory.Object,
            registry.Object,
            hostEnvironment,
            Mock.Of<IEventBus>(),
            _shutdownSignal,
            TimeProvider.System,
            NullLogger<DefaultJobRuntime>.Instance,
            TimeSpan.FromMilliseconds(25));

        // Act
        await subject.ExecuteAsync(executionId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            retryPolicy.Verify(policy => policy.BuildRetryAsync(execution, It.IsAny<CancellationToken>()), Times.Once);
            executions.Verify(
                repository => repository.CreateAsync(
                    retry,
                    userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(retry.Attempt, Is.EqualTo(2));
            Assert.That(retry.NotBeforeAt, Is.Not.Null);
        }
    }
}
