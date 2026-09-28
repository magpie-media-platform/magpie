using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Core.Jobs.Runtime;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class JobExecutionScopeTests
{
    private ServiceProvider _services = null!;
    private Mock<IProgressReporter> _progress = null!;

    [SetUp]
    public void SetUp()
    {
        _progress = new Mock<IProgressReporter>();
        _services = new ServiceCollection().AddSingleton(_progress.Object).BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
    }

    [Test]
    public async Task FlushProgressAsync_WaitsForTheScopeReporter_WhenWritesArePending()
    {
        // Arrange
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        _progress.Setup(reporter => reporter.FlushAsync(cancellation.Token)).Returns(written.Task);
        var factory = new JobExecutionScope(_services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IJobRegistry>(), NullLoggerFactory.Instance);
        var scope = factory.CreateScope(Guid.NewGuid());
        await using var scopeHandle = scope.ConfigureAwait(false);

        // Act
        var flush = scope.FlushProgressAsync(cancellation.Token);
        var completedBeforeWrite = flush.IsCompleted;
        written.SetResult();
        await flush.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(completedBeforeWrite, Is.False);
            _progress.Verify(reporter => reporter.FlushAsync(cancellation.Token), Times.Once);
        }
    }

    [Test]
    public async Task CreateScope_KeepsTheAmbientCorrelation_WhenTheCallerSetsAParentContext()
    {
        // Arrange
        var parentExecutionId = Guid.NewGuid();
        var childExecutionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var factory = new JobExecutionScope(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IJobRegistry>(),
            NullLoggerFactory.Instance);
        factory.SetParentContext(parentExecutionId, correlationId);

        // Act
        var scope = factory.CreateScope(childExecutionId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.CurrentCorrelationId, Is.EqualTo(correlationId));
            Assert.That(factory.CurrentExecutionId, Is.EqualTo(childExecutionId));
        }

        await scope.DisposeAsync().ConfigureAwait(false);
    }
}
