using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Qbittorrent;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public sealed class QbittorrentCategoryDiagnosticTests
{
    private Mock<ISettingsService> _settingsService = null!;
    private DiagnosticContext _context = null!;
    private IDiagnosticCheck _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsService = new Mock<ISettingsService>();
        _context = new DiagnosticContext
        {
            DownloadClients =
            [
                new DownloadClient
                {
                    Id = new Guid("00000000-0000-0000-0000-000000000001"),
                    Name = "Seedbox",
                    ProviderType = "qbittorrent",
                    Enabled = true,
                },
            ],
        };
        _subject = new QbittorrentCategoryDiagnostic(_settingsService.Object);
    }

    [Test]
    public async Task RunAsync_NamesTheInstance_WhenCategoryIsMissing()
    {
        // Arrange
        _settingsService
            .Setup(service => service.GetInstanceNameAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Home Lab");

        // Act
        var findings = await _subject.RunAsync(_context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(
                findings[0].Detail,
                Is.EqualTo(
                    "The qBittorrent client \"Seedbox\" has no category. " +
                    "A category keeps Home Lab downloads separate for filtering and cleanup."));
        }
    }
}
