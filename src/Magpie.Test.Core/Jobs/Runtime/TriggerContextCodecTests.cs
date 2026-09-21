using System;
using System.Collections.Generic;

using Magpie.Models;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class TriggerContextCodecTests
{
    [Test]
    public void ToTriggerContext_WritesEventFieldsAndPrefixedContext_WhenEventIsProvided()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var jobExecutionId = Guid.NewGuid();
        var magpieEvent = new MagpieEvent(
            eventId,
            "download.completed",
            new Dictionary<string, string>
            {
                ["download.id"] = "download-1",
                ["release.id"] = "release-1"
            },
            new DateTime(2026, 6, 19, 12, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 19, 12, 30, 1, DateTimeKind.Utc),
            "node-a")
        {
            JobExecutionId = jobExecutionId
        };

        // Act
        var triggerContext = magpieEvent.ToTriggerContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(triggerContext["event.id"], Is.EqualTo(eventId.ToString("D")));
            Assert.That(triggerContext["event.name"], Is.EqualTo("download.completed"));
            Assert.That(triggerContext["event.occurredAt"], Is.EqualTo("2026-06-19T12:30:00.0000000Z"));
            Assert.That(triggerContext["event.publishedAt"], Is.EqualTo("2026-06-19T12:30:01.0000000Z"));
            Assert.That(triggerContext["event.nodeId"], Is.EqualTo("node-a"));
            Assert.That(triggerContext["event.jobExecutionId"], Is.EqualTo(jobExecutionId.ToString("D")));
            Assert.That(triggerContext["event.context.download.id"], Is.EqualTo("download-1"));
            Assert.That(triggerContext["event.context.release.id"], Is.EqualTo("release-1"));
        }
    }

    [Test]
    public void ToMagpieEvent_RehydratesEvent_WhenTriggerContextIsValid()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var jobExecutionId = Guid.NewGuid();
        var triggerContext = new Dictionary<string, string>
        {
            ["event.id"] = eventId.ToString("D"),
            ["event.name"] = "root-folder.created",
            ["event.occurredAt"] = "2026-06-19T12:30:00.0000000Z",
            ["event.publishedAt"] = "2026-06-19T12:30:01.0000000Z",
            ["event.nodeId"] = "node-a",
            ["event.jobExecutionId"] = jobExecutionId.ToString("D"),
            ["event.context.rootFolderId"] = "root-1",
            ["parameter"] = "ignored"
        };

        // Act
        var magpieEvent = triggerContext.ToMagpieEvent();

        // Assert
        Assert.That(magpieEvent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(magpieEvent!.Id, Is.EqualTo(eventId));
            Assert.That(magpieEvent.Name, Is.EqualTo("root-folder.created"));
            Assert.That(magpieEvent.OccurredAt, Is.EqualTo(new DateTime(2026, 6, 19, 12, 30, 0, DateTimeKind.Utc)));
            Assert.That(magpieEvent.PublishedAt, Is.EqualTo(new DateTime(2026, 6, 19, 12, 30, 1, DateTimeKind.Utc)));
            Assert.That(magpieEvent.NodeId, Is.EqualTo("node-a"));
            Assert.That(magpieEvent.JobExecutionId, Is.EqualTo(jobExecutionId));
            Assert.That(
                magpieEvent.Context,
                Is.EquivalentTo(new Dictionary<string, string>
                {
                    ["rootFolderId"] = "root-1"
                }));
        }
    }

    [Test]
    public void ToTriggerContext_WritesCorrelationIdAndUnprefixedTraceFields_WhenEventCarriesProvenance()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            new DateTime(2026, 6, 19, 12, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 19, 12, 30, 1, DateTimeKind.Utc),
            "node-a")
        {
            CorrelationId = correlationId,
            TraceContext = new Dictionary<string, string>
            {
                ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                ["tracestate"] = "magpie=1"
            }
        };

        // Act
        var triggerContext = magpieEvent.ToTriggerContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(triggerContext["event.correlationId"], Is.EqualTo(correlationId.ToString("D")));
            Assert.That(
                triggerContext["traceparent"],
                Is.EqualTo("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"));
            Assert.That(triggerContext["tracestate"], Is.EqualTo("magpie=1"));
        }
    }

    [Test]
    public void ToTriggerContext_KeepsTheEventFields_WhenTraceContextNamesAReservedKey()
    {
        // Arrange
        var eventId = new Guid("33333333-3333-3333-3333-333333333333");
        var magpieEvent = new MagpieEvent(
            eventId,
            "download.completed",
            new Dictionary<string, string>(),
            new DateTime(2026, 6, 19, 12, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 19, 12, 30, 1, DateTimeKind.Utc),
            "node-a")
        {
            TraceContext = new Dictionary<string, string>
            {
                ["event.id"] = "not-a-guid",
                ["event.context.download.id"] = "hijacked",
                ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
            }
        };

        // Act
        var triggerContext = magpieEvent.ToTriggerContext();
        var roundTripped = ((IReadOnlyDictionary<string, string>)triggerContext).ToMagpieEvent();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                triggerContext["event.id"],
                Is.EqualTo("33333333-3333-3333-3333-333333333333"));
            Assert.That(triggerContext.ContainsKey("event.context.download.id"), Is.False);
            Assert.That(
                triggerContext["traceparent"],
                Is.EqualTo("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"));
            Assert.That(roundTripped?.Id, Is.EqualTo(eventId));
        }
    }

    [Test]
    public void ToMagpieEvent_RehydratesCorrelationId_WhenTriggerContextCarriesIt()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var triggerContext = new Dictionary<string, string>
        {
            ["event.id"] = Guid.NewGuid().ToString("D"),
            ["event.name"] = "download.completed",
            ["event.occurredAt"] = "2026-06-19T12:30:00.0000000Z",
            ["event.publishedAt"] = "2026-06-19T12:30:01.0000000Z",
            ["event.correlationId"] = correlationId.ToString("D")
        };

        // Act
        var magpieEvent = triggerContext.ToMagpieEvent();

        // Assert
        Assert.That(magpieEvent, Is.Not.Null);
        Assert.That(magpieEvent!.CorrelationId, Is.EqualTo(correlationId));
    }

    [Test]
    public void ToMagpieEvent_ReturnsNullCorrelationId_WhenTriggerContextOmitsIt()
    {
        // Arrange
        var triggerContext = new Dictionary<string, string>
        {
            ["event.id"] = Guid.NewGuid().ToString("D"),
            ["event.name"] = "download.completed",
            ["event.occurredAt"] = "2026-06-19T12:30:00.0000000Z",
            ["event.publishedAt"] = "2026-06-19T12:30:01.0000000Z"
        };

        // Act
        var magpieEvent = triggerContext.ToMagpieEvent();

        // Assert
        Assert.That(magpieEvent, Is.Not.Null);
        Assert.That(magpieEvent!.CorrelationId, Is.Null);
    }

    [Test]
    public void ToMagpieEvent_ReturnsNullNodeId_WhenNodeIdIsBlank()
    {
        // Arrange
        var triggerContext = new Dictionary<string, string>
        {
            ["event.id"] = Guid.NewGuid().ToString("D"),
            ["event.name"] = "content.added",
            ["event.occurredAt"] = "2026-06-19T12:30:00.0000000Z",
            ["event.publishedAt"] = "2026-06-19T12:30:01.0000000Z",
            ["event.nodeId"] = " "
        };

        // Act
        var magpieEvent = triggerContext.ToMagpieEvent();

        // Assert
        Assert.That(magpieEvent, Is.Not.Null);
        Assert.That(magpieEvent!.NodeId, Is.Null);
    }

    [Test]
    public void ToMagpieEvent_ReturnsNull_WhenRequiredEventFieldIsMalformed()
    {
        // Arrange
        var triggerContext = new Dictionary<string, string>
        {
            ["event.id"] = "not-a-guid",
            ["event.name"] = "content.added",
            ["event.occurredAt"] = "2026-06-19T12:30:00.0000000Z",
            ["event.publishedAt"] = "2026-06-19T12:30:01.0000000Z"
        };

        // Act
        var magpieEvent = triggerContext.ToMagpieEvent();

        // Assert
        Assert.That(magpieEvent, Is.Null);
    }
}
