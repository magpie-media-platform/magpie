using Magpie.Sdk.Jobs;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class ProgressReporterExtensionsTests
{
    [TestCase(-1, 4, 0d)]
    [TestCase(0, 4, 0d)]
    [TestCase(1, 4, 0.25d)]
    [TestCase(4, 4, 1d)]
    [TestCase(5, 4, 1d)]
    [TestCase(1, 0, null)]
    [TestCase(1, -1, null)]
    public void ReportFraction_ReportsExpectedProgress_WhenCountsAreProvided(
        int completed,
        int total,
        double? expected)
    {
        // Arrange
        var reporter = new Mock<IProgressReporter>();

        // Act
        reporter.Object.ReportFraction(completed, total, "Current step.");

        // Assert
        reporter.Verify(value => value.Report(expected, "Current step."), Times.Once);
    }
}
