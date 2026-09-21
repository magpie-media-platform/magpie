using System;

using Magpie.Core.Jobs.Runtime;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class CronScheduleTests
{
    [Test]
    public void TryParse_ReturnsSchedule_WhenScheduleIsTrimmed()
    {
        // Arrange
        var fromUtc = new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc);

        // Act
        var parsed = CronSchedule.TryParse(" 0 * * * * ", out var schedule, out var error);
        var next = schedule.NextOccurrence(fromUtc);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(next, Is.EqualTo(new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc)));
        }
    }

    [TestCase("0 * * * *", true)]
    [TestCase("", false)]
    [TestCase("99 99 * * *", false)]
    public void IsValid_ReportsValidity_WhenScheduleSyntaxVaries(string schedule, bool expected)
    {
        // Act
        var valid = CronSchedule.IsValid(schedule);

        // Assert
        Assert.That(valid, Is.EqualTo(expected));
    }

    [Test]
    public void NextOccurrence_UsesUtc_WhenSourceTimeIsUnspecified()
    {
        // Arrange
        _ = CronSchedule.TryParse("0 * * * *", out var schedule, out _);
        var from = new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Unspecified);

        // Act
        var next = schedule.NextOccurrence(from);

        // Assert
        Assert.That(next, Is.EqualTo(new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc)));
    }
}
