using System;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Core.Jobs.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class JobExecutionScopeTests
{
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
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
