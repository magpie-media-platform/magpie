using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class MissingDefaultJobRepairTests
{
    private Mock<IScheduleTriggerRepository> _scheduleTriggers = null!;
    private Mock<IEventTriggerRepository> _eventTriggers = null!;
    private MissingDefaultJobRepair _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _scheduleTriggers = new Mock<IScheduleTriggerRepository>();
        _scheduleTriggers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _eventTriggers = new Mock<IEventTriggerRepository>();
        _eventTriggers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new EventTrigger
                {
                    JobType = "ContentFulfillmentReconciliation",
                    Scope = JobScope.Administrative,
                    Name = "Content fulfillment reconciliation on quality tier list change",
                    EventFilter = new(),
                    Enabled = true
                }
            ]);
        _subject = new MissingDefaultJobRepair(
            _scheduleTriggers.Object,
            _eventTriggers.Object,
            NullLogger<MissingDefaultJobRepair>.Instance);
    }

    [Test]
    public async Task RunAsync_CreatesOnlyTheMissingEventTriggersOfTheJobType_WhenTheFindingNamesAnEventTriggerJob()
    {
        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "core.config.default-job-schedule",
            Title = "test",
            Detail = "test",
            AffectedEntity = "EventTriggerJob:ContentFulfillmentReconciliation",
            RepairId = "core.config.reseed-default-job",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            _eventTriggers.Verify(
                repository => repository.CreateAsync(
                    It.Is<EventTrigger>(trigger =>
                        trigger.Name == "Content fulfillment reconciliation on quality tier change"),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventTriggers.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<EventTrigger>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _scheduleTriggers.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ScheduleTrigger>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task RunAsync_CreatesTheSchedulesOfTheJobType_WhenTheFindingNamesAJob()
    {
        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "core.config.default-job-schedule",
            Title = "test",
            Detail = "test",
            AffectedEntity = "Job:DownloadCleanup",
            RepairId = "core.config.reseed-default-job",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            _scheduleTriggers.Verify(
                repository => repository.CreateAsync(
                    It.Is<ScheduleTrigger>(trigger =>
                        trigger.Name == "Download cleanup schedule" && trigger.Schedule == "0 */6 * * *"),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _scheduleTriggers.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ScheduleTrigger>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventTriggers.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<EventTrigger>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
