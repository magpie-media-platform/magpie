using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Jobs.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class ProgressReporterTests
{
    private ConcurrentQueue<string?> _appliedMessages = null!;
    private Guid _executionId;
    private Mock<IJobExecutionScopeFactory> _executionScopes = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private ServiceProvider _scopeServices = null!;
    private Mock<IServiceScope> _scope = null!;
    private Mock<IServiceScopeFactory> _scopes = null!;

    [SetUp]
    public void SetUp()
    {
        _executionId = Guid.NewGuid();
        _appliedMessages = [];
        _executions = new Mock<IJobExecutionRepository>();
        _scopeServices = new ServiceCollection()
            .AddSingleton(_executions.Object)
            .BuildServiceProvider();
        _scope = new Mock<IServiceScope>();
        _scope.SetupGet(candidate => candidate.ServiceProvider).Returns(_scopeServices);
        _scopes = new Mock<IServiceScopeFactory>();
        _scopes.Setup(factory => factory.CreateScope()).Returns(_scope.Object);
        _executionScopes = new Mock<IJobExecutionScopeFactory>();
        _executionScopes.SetupGet(scopes => scopes.CurrentExecutionId).Returns(() => _executionId);
    }

    [TearDown]
    public void TearDown()
    {
        _scopeServices.Dispose();
    }

    [Test]
    public async Task Report_SkipsTheSupersededReports_WhenNewerReportsArriveBeforeTheWriteStarts()
    {
        // Arrange
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executions
            .Setup(repository => repository.UpdateProgressAsync(
                It.IsAny<Guid>(),
                It.IsAny<double?>(),
                "step 1",
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                firstWriteStarted.SetResult();
                await releaseFirstWrite.Task.ConfigureAwait(false);
                _appliedMessages.Enqueue("step 1");
                return true;
            });
        _executions
            .Setup(repository => repository.UpdateProgressAsync(
                It.IsAny<Guid>(),
                It.IsAny<double?>(),
                It.Is<string?>(message => message != "step 1"),
                It.IsAny<CancellationToken>()))
            .Callback((Guid _, double? _, string? message, CancellationToken _) => _appliedMessages.Enqueue(message))
            .ReturnsAsync(true);
        var subject = new ProgressReporter(
            _executionScopes.Object,
            _scopes.Object,
            NullLogger<ProgressReporter>.Instance,
            new JobRuntimeOptions());

        // Act
        subject.Report(0.25, "step 1");
        await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        subject.Report(0.5, "step 2");
        subject.Report(0.75, "step 3");
        subject.Report(1, "step 4");
        releaseFirstWrite.SetResult();
        await subject.DisposeAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_appliedMessages, Is.EqualTo(["step 1", "step 4"]));
    }

    [Test]
    public async Task DisposeAsync_ReturnsBeforeTheWriteCompletes_WhenTheFlushExceedsItsBudget()
    {
        // Arrange
        var write = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _executions
            .Setup(repository => repository.UpdateProgressAsync(
                It.IsAny<Guid>(),
                It.IsAny<double?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(write.Task);
        var subject = new ProgressReporter(
            _executionScopes.Object,
            _scopes.Object,
            NullLogger<ProgressReporter>.Instance,
            new JobRuntimeOptions { ProgressFlushTimeout = TimeSpan.FromMilliseconds(50) });

        // Act
        subject.Report(0.5, "step 1");
        await subject.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Task.IsCompleted, Is.False);
            _executions.Verify(repository => repository.UpdateProgressAsync(
                _executionId, 50, "step 1", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task Report_DisposesTheScope_WhenTheProgressWriteFails()
    {
        // Arrange
        var failure = new InvalidOperationException("The progress write failed.");
        _executions
            .Setup(repository => repository.UpdateProgressAsync(
                It.IsAny<Guid>(),
                It.IsAny<double?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(failure);
        var subject = new ProgressReporter(
            _executionScopes.Object,
            _scopes.Object,
            NullLogger<ProgressReporter>.Instance,
            new JobRuntimeOptions());

        // Act
        subject.Report(0.5, "step");
        await subject.DisposeAsync().ConfigureAwait(false);

        // Assert
        _scope.Verify(candidate => candidate.Dispose(), Times.Once);
    }
}
