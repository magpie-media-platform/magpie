using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

using Microsoft.Extensions.Configuration;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class DefaultJobScheduleDiagnosticTests
{
    private Mock<IConfiguration> _configuration = null!;
    private string? _minimumSearchAgeHours;
    private ScheduleTrigger _releaseSearchTrigger = null!;
    private IDiagnosticCheck _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _minimumSearchAgeHours = null;
        _configuration = new Mock<IConfiguration>();
        _configuration
            .SetupGet(configuration => configuration["Magpie:Search:MinimumSearchAgeHours"])
            .Returns(() => _minimumSearchAgeHours);
        _releaseSearchTrigger = new ScheduleTrigger
        {
            Id = Guid.NewGuid(),
            JobType = ReleaseSearchScanJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Release search scan schedule",
            Schedule = "*/30 * * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        };

        var scheduleTriggers = new Mock<IScheduleTriggerRepository>();
        scheduleTriggers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_releaseSearchTrigger]);

        var eventTriggers = new Mock<IEventTriggerRepository>();
        eventTriggers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var jobRegistry = new Mock<IJobRegistry>();
        var spec = ReleaseSearchScanJob.Spec;
        jobRegistry
            .Setup(registry => registry.TryGetJobSpec(ReleaseSearchScanJob.Spec.JobType, out spec))
            .Returns(true);

        _subject = new DefaultJobScheduleDiagnostic(
            scheduleTriggers.Object,
            eventTriggers.Object,
            jobRegistry.Object,
            _configuration.Object);
    }

    [Test]
    public async Task RunAsync_DoesNotRequireRemovedJobParameter_ForReleaseSearchScan()
    {
        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings,
            Has.None.Matches<DiagnosticFinding>(finding =>
                finding.Title.Contains("minimumSearchAgeHours", StringComparison.Ordinal)));
    }

    [Test]
    public async Task RunAsync_UsesConfiguredMinimumSearchAge_ForReleaseSearchScanCadence()
    {
        // Arrange
        _minimumSearchAgeHours = "1";
        _releaseSearchTrigger.Schedule = "0 */2 * * *";

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings.SingleOrDefault(finding =>
                finding.Title == "ReleaseSearchScan schedule runs less often than " +
                    "Magpie:Search:MinimumSearchAgeHours"),
            Is.Not.Null);
    }
}
