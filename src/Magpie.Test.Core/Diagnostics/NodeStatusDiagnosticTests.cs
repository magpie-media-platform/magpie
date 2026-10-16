using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class NodeStatusDiagnosticTests
{
    private Mock<IExtensionNodeStatusRepository> _nodeStatuses = null!;
    private NodeStatusDiagnostic _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _nodeStatuses = new Mock<IExtensionNodeStatusRepository>();
        _subject = new NodeStatusDiagnostic(
            _nodeStatuses.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero)));
    }

    [TestCase(
        ExtensionNodeHealth.StaleRequiresRestart,
        "Extension changes require a restart. No supervisor can restart this node, so restart it manually.",
        "Node magpie-1 is pending restart")]
    [TestCase(
        ExtensionNodeHealth.CurrentHealthy,
        "The database schema is newer than this node. Unknown migrations: 20991231000000_Future.",
        "Node magpie-1 takes no jobs")]
    [TestCase(
        ExtensionNodeHealth.ActivationFailed,
        "Extension Test failed to load.",
        "Node magpie-1 takes no jobs")]
    [TestCase(
        ExtensionNodeHealth.Offline,
        "Extension activation status is unavailable.",
        "Node magpie-1 takes no jobs")]
    public async Task RunAsync_ReportsTheNodeWithItsReason_WhenTheNodeTakesNoJobs(
        ExtensionNodeHealth health,
        string message,
        string expectedTitle)
    {
        // Arrange
        _nodeStatuses.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ExtensionNodeStatus
            {
                NodeId = "magpie-1",
                AppliedGeneration = 1,
                ExtensionHealth = health,
                SchedulerEligible = false,
                LastSeenAt = new DateTime(2026, 10, 12, 11, 59, 30, DateTimeKind.Utc),
                Message = message
            },
        ]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].DiagnosticCheckId, Is.EqualTo("core.nodes.status"));
            Assert.That(findings[0].Severity, Is.EqualTo(DiagnosticFindingSeverity.Warning));
            Assert.That(findings[0].Title, Is.EqualTo(expectedTitle));
            Assert.That(findings[0].Detail, Is.EqualTo(message));
            Assert.That(findings[0].AffectedEntity, Is.EqualTo("Node:magpie-1"));
        }
    }

    [TestCase(ExtensionNodeHealth.CurrentHealthy, true, 59)]
    [TestCase(ExtensionNodeHealth.ActivationFailed, true, 59)]
    [TestCase(ExtensionNodeHealth.StaleRequiresRestart, false, 57)]
    public async Task RunAsync_ReportsNothing_WhenTheNodeTakesJobsOrSentNoRecentHeartbeat(
        ExtensionNodeHealth health,
        bool schedulerEligible,
        int lastSeenMinute)
    {
        // Arrange
        _nodeStatuses.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ExtensionNodeStatus
            {
                NodeId = "magpie-1",
                AppliedGeneration = 1,
                ExtensionHealth = health,
                SchedulerEligible = schedulerEligible,
                LastSeenAt = new DateTime(2026, 10, 12, 11, lastSeenMinute, 0, DateTimeKind.Utc),
                Message = "Extension activation failed."
            },
        ]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }
}
