using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public sealed class ApiKeyMissingDiagnosticTests
{
    [Test]
    public async Task RunAsync_ProducesFinding_WhenApiKeyIsMissing()
    {
        // Arrange
        var resolver = new Mock<ISettingsResolver>();
        resolver.Setup(subject => subject.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings());
        var subject = new ApiKeyMissingDiagnostic(resolver.Object);

        // Act
        var findings = await subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].DiagnosticCheckId, Is.EqualTo(ApiKeyMissingDiagnostic.Id));
            Assert.That(findings[0].Severity, Is.EqualTo(DiagnosticFindingSeverity.Warning));
            Assert.That(
                findings[0].AffectedEntity,
                Is.EqualTo("Extension:fe120126-efd7-4db1-a3a1-8b8a881e013a"));
        }
    }

    [Test]
    public async Task RunAsync_ProducesNoFinding_WhenApiKeyIsConfigured()
    {
        // Arrange
        var resolver = new Mock<ISettingsResolver>();
        resolver.Setup(subject => subject.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings { ApiKey = "configured-key" });
        var subject = new ApiKeyMissingDiagnostic(resolver.Object);

        // Act
        var findings = await subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }
}
