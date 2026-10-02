using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class MissingDefaultJobRepairTests
{
    private static readonly Guid TriggerId = new("00000000-0000-0000-0000-000000000300");

    private Mock<IEventTriggerRepository> _eventTriggers = null!;
    private MissingDefaultJobRepair _subject = null!;

    public static IEnumerable<TestCaseData> UntouchedTriggerCases
    {
        get
        {
            yield return new TestCaseData(new EventTrigger
            {
                Id = TriggerId,
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true,
                Parameters = new() { ["mediaInfoPolicy"] = "All" }
            });
            yield return new TestCaseData(new EventTrigger
            {
                Id = TriggerId,
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "My media info scan",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true,
                CreatedByUserId = new Guid("00000000-0000-0000-0000-00000000000a")
            });
            yield return new TestCaseData(new EventTrigger
            {
                Id = TriggerId,
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "download.imported" }
                },
                Enabled = true
            });
        }
    }

    [SetUp]
    public void SetUp()
    {
        _eventTriggers = new Mock<IEventTriggerRepository>();
        _eventTriggers
            .Setup(repository => repository.PatchAsync(
                It.IsAny<EventTriggerPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _subject = new MissingDefaultJobRepair(
            Mock.Of<IScheduleTriggerRepository>(),
            _eventTriggers.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<MissingDefaultJobRepair>.Instance);
    }

    [Test]
    public async Task RunAsync_BindsTheBuiltInParameters_WhenTheStoredImportTriggerHasNoParameters()
    {
        // Arrange
        _eventTriggers
            .Setup(repository => repository.GetByIdAsync(TriggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = TriggerId,
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true
            });

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "core.config.default-job-schedule",
            Title = "test",
            Detail = "test",
            AffectedEntity = "EventTrigger:00000000-0000-0000-0000-000000000300",
            RepairId = "core.config.reseed-default-job",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            _eventTriggers.Verify(
                repository => repository.PatchAsync(
                    It.Is<EventTriggerPatch>(patch =>
                        patch.Id == TriggerId &&
                        patch.Parameters!.Add!.Count == 2 &&
                        patch.Parameters.Add["contentHierarchyItemId"] == "${event.content.hierarchyItemId}" &&
                        patch.Parameters.Add["mediaInfoPolicy"] == "Missing"),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCaseSource(nameof(UntouchedTriggerCases))]
    public async Task RunAsync_LeavesTheTrigger_WhenItIsNotTheBuiltInTriggerWithNoParameters(EventTrigger stored)
    {
        // Arrange
        _eventTriggers
            .Setup(repository => repository.GetByIdAsync(TriggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "core.config.default-job-schedule",
            Title = "test",
            Detail = "test",
            AffectedEntity = "EventTrigger:00000000-0000-0000-0000-000000000300",
            RepairId = "core.config.reseed-default-job",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            _eventTriggers.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<EventTriggerPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task RunAsync_Fails_WhenAUserModifiedTheTrigger()
    {
        // Arrange
        _eventTriggers
            .Setup(repository => repository.GetByIdAsync(TriggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = TriggerId,
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true,
                ModifiedByUserId = new Guid("00000000-0000-0000-0000-00000000000a")
            });

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "core.config.default-job-schedule",
            Title = "test",
            Detail = "test",
            AffectedEntity = "EventTrigger:00000000-0000-0000-0000-000000000300",
            RepairId = "core.config.reseed-default-job",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Message,
                Is.EqualTo("Event trigger Media info extract on import was modified by a user."));
            _eventTriggers.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<EventTriggerPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
