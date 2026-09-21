using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Events;

[TestFixture]
public sealed class ExtensionActivationBackgroundServiceTests
{
    [TestCase(true, 1, ExtensionNodeHealth.StaleRequiresRestart, false)]
    [TestCase(false, 1, ExtensionNodeHealth.StaleRequiresRestart, true)]
    [TestCase(true, 2, ExtensionNodeHealth.CurrentHealthy, true)]
    public async Task ExecuteAsync_ReportsEligibility_WhenActivationGenerationChanges(
        bool clustered, long applied, ExtensionNodeHealth expectedHealth, bool eligible)
    {
        // Arrange
        var environment = new MagpieHostEnvironment("local", clustered, false, "", "", "", "", DateTime.UtcNow);
        var statuses = new Mock<IExtensionNodeStatusRepository>();
        statuses.Setup(value => value.GetByNodeIdAsync("local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtensionNodeStatus
            {
                NodeId = "local",
                AppliedGeneration = applied,
                ExtensionHealth = ExtensionNodeHealth.CurrentHealthy,
            });
        var written = new TaskCompletionSource<ExtensionNodeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        statuses.Setup(value => value.UpsertAsync(It.IsAny<ExtensionNodeStatus>(), It.IsAny<CancellationToken>()))
            .Callback<ExtensionNodeStatus, CancellationToken>((status, _) => written.TrySetResult(status));
        var generations = new Mock<IExtensionActivationGenerationRepository>();
        generations.Setup(value => value.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var services = new ServiceCollection();
        services.AddSingleton(statuses.Object);
        services.AddSingleton(generations.Object);
        using var provider = services.BuildServiceProvider();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero));
        using var bus = new EventBus(environment, Mock.Of<IJobExecutionScopeFactory>(), timeProvider);
        var migration = new DatabaseMigrationState();
        using var subject = new ExtensionActivationBackgroundService(bus,
            provider.GetRequiredService<IServiceScopeFactory>(), environment, timeProvider,
            NullLogger<ExtensionActivationBackgroundService>.Instance, migration);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await bus.PublishAsync(ExtensionEventBuilder.BuildExtensionActivationChangedEvent(2)).ConfigureAwait(false);
        migration.Complete();
        var status = await written.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.ExtensionHealth, Is.EqualTo(expectedHealth));
            Assert.That(status.SchedulerEligible, Is.EqualTo(eligible));
            Assert.That(status.LastSeenAt, Is.EqualTo(new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc)));
        }
    }
}
