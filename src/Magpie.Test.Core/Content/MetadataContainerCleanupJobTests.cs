using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class MetadataContainerCleanupJobTests
{
    private Mock<IMetadataContainerRepository> _metadataContainerRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _metadataContainerRepository = new Mock<IMetadataContainerRepository>();
        _metadataContainerRepository
            .Setup(r => r.ListOrphanedContainerIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Test]
    public async Task ExecuteAsync_DeletesOrphansAndReportsProgress_WhenOrphanedContainersExist()
    {
        // Arrange
        var orphanContainerId = Guid.NewGuid();
        _metadataContainerRepository
            .Setup(r => r.ListOrphanedContainerIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([orphanContainerId]);

        var progressReports = new List<ProgressReport>();
        var progress = new Mock<IProgressReporter>(MockBehavior.Strict);
        progress
            .Setup(x => x.Report(It.IsAny<double?>(), It.IsAny<string?>()))
            .Callback((double? percentage, string? message) =>
                progressReports.Add(new ProgressReport(percentage, message)));
        var subject = new MetadataContainerCleanupJob(_metadataContainerRepository.Object, progress.Object);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(progressReports, Has.Count.EqualTo(2));
            Assert.That(progressReports[0].Percentage, Is.Zero);
            Assert.That(progressReports[1].Percentage, Is.EqualTo(1));
            Assert.That(progressReports[1].Message, Does.Contain("Deleted containers: 1."));
        }

        _metadataContainerRepository.Verify(
            r => r.RemoveContainersAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == orphanContainerId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private readonly record struct ProgressReport(double? Percentage, string? Message);
}
