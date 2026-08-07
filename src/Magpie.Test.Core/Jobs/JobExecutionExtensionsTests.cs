using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class JobExecutionExtensionsTests
{
    [Test]
    public void ToRequeue_PreservesLineage_WhenSourceHasParentExecution()
    {
        // Arrange
        var parentExecutionId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var source = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "TestJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Failed,
            ParentExecutionId = parentExecutionId,
            CorrelationId = Guid.NewGuid(),
            LockPartition = "TestJob",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var requeue = source.ToRequeue(originalId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requeue.ParentExecutionId, Is.EqualTo(parentExecutionId));
            Assert.That(requeue.OriginalExecutionId, Is.EqualTo(originalId));
            Assert.That(requeue.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(requeue.CorrelationId, Is.EqualTo(source.CorrelationId));
            Assert.That(requeue.LockPartition, Is.EqualTo(source.LockPartition));
        }
    }
}
