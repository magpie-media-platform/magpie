using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Core.Extensions;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class ExtensionLoadDiagnosticTests
{
    private Mock<IExtensionRepository> _extensions = null!;
    private Extension _failed = null!;
    private ExtensionLoadDiagnostic _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _failed = new Extension
        {
            Id = new Guid("00000000-0000-0000-0000-0000000000b1"),
            Name = "Books",
            Version = "1.4.0",
            FilePath = "/app/data/extensions/Books/lib/net10.0/Books.dll",
            AssemblyName = "Books",
            Installed = true,
            Enabled = true,
        };
        _extensions = new Mock<IExtensionRepository>();
        _extensions.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_failed]);
        _subject = new ExtensionLoadDiagnostic(new ExtensionLoadContext([], [_failed]), _extensions.Object);
    }

    [Test]
    public async Task RunAsync_ReportsAnError_WhenAnExtensionFailedToLoad()
    {
        // Arrange
        var context = DiagnosticContext.Empty;

        // Act
        var findings = await _subject.RunAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].DiagnosticCheckId, Is.EqualTo("core.extensions.load-failure"));
            Assert.That(findings[0].Severity, Is.EqualTo(DiagnosticFindingSeverity.Error));
            Assert.That(findings[0].Title, Is.EqualTo("Extension Books failed to load"));
            Assert.That(findings[0].Detail, Is.EqualTo("/app/data/extensions/Books/lib/net10.0/Books.dll"));
            Assert.That(findings[0].AffectedEntity, Is.EqualTo("Extension:00000000-0000-0000-0000-0000000000b1"));
        }
    }

    [Test]
    public async Task RunAsync_ReportsNothing_WhenEveryExtensionLoaded()
    {
        // Arrange
        var subject = new ExtensionLoadDiagnostic(new ExtensionLoadContext([], []), _extensions.Object);

        // Act
        var findings = await subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }

    [Test]
    public async Task RunAsync_ReportsNothing_WhenTheFailedExtensionIsDisabled()
    {
        // Arrange
        _extensions.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_failed with { Enabled = false }]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }

    [Test]
    public async Task RunAsync_ReportsNothing_WhenTheFailedExtensionIsUninstalled()
    {
        // Arrange
        _extensions.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }
}
