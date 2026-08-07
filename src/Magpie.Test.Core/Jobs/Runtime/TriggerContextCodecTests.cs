using System;
using System.Collections.Generic;

using Magpie.Models;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class TriggerContextCodecTests
{
    [Test]
    public void Serialize_WritesEventFieldsAndPrefixedContext_WhenEventIsProvided()
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
    public void Deserialize_RehydratesEvent_WhenTriggerContextIsValid()
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
    public void Deserialize_ReturnsNullNodeId_WhenNodeIdIsBlank()
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
    public void Deserialize_ReturnsNull_WhenRequiredEventFieldIsMalformed()
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
