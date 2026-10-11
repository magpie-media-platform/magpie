using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Jellyfin;

[TestFixture]
public sealed class JellyfinLibraryMappingDiagnosticTests
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
            MediaServers =
            [
                new MediaServer
                {
                    Id = new Guid("00000000-0000-0000-0000-000000000001"),
                    Name = "Living room",
                    ProviderType = "jellyfin",
                    Enabled = true,
                    Settings = { ["libraryMappings"] = "[{\"rootFolderPath\":\"/data/other\"}]" },
                },
            ],
        };
        _subject = new JellyfinLibraryMappingDiagnostic(
            NullLogger<JellyfinLibraryMappingDiagnostic>.Instance,
            _settingsService.Object);
    }

    [Test]
    public async Task RunAsync_NamesTheInstance_WhenLibraryMappingPointsOutsideTheRootFolders()
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
                    "The Jellyfin library mapping points to /data/other, which is not a configured " +
                    "Home Lab root folder. Library refreshes and links will apply to the entire server."));
        }
    }
}
