using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Integrations;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class SetupPrerequisitesDiagnosticTests
{
    private Mock<IIndexerConnectionRepository> _indexerConnections = null!;
    private List<DownloadClient> _downloadClients = null!;
    private Mock<IRootFolderRepository> _rootFolders = null!;
    private List<IDownloadClientIntegration> _downloadClientIntegrations = null!;
    private IDiagnosticCheck _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _indexerConnections = new Mock<IIndexerConnectionRepository>();
        _indexerConnections
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _downloadClients = [];
        _rootFolders = new Mock<IRootFolderRepository>();
        _rootFolders
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _downloadClientIntegrations = [];

        _subject = new SetupPrerequisitesDiagnostic(
            _indexerConnections.Object,
            _rootFolders.Object,
            _downloadClientIntegrations);
    }

    [Test]
    public async Task RunAsync_ProducesAFindingPerPrerequisite_WhenNothingIsConfigured()
    {
        // Act
        var findings = await _subject.RunAsync(new() { DownloadClients = _downloadClients, }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(3));
            Assert.That(findings, Has.All.Matches<DiagnosticFinding>(f => f.RepairId is null));
            Assert.That(
                findings,
                Has.Exactly(2).Matches<DiagnosticFinding>(f => f.Severity == DiagnosticFindingSeverity.Error));
            Assert.That(
                findings,
                Has.Exactly(1).Matches<DiagnosticFinding>(f => f.Severity == DiagnosticFindingSeverity.Warning));
        }
    }

    [Test]
    public async Task RunAsync_ProducesNoFinding_WhenEveryPrerequisiteIsPresent()
    {
        // Arrange
        _indexerConnections
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = Guid.NewGuid(),
                    Name = "Test connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _downloadClients.Add(new DownloadClient
        {
            Id = Guid.NewGuid(),
            ProviderType = "downloadclient-a",
            Name = "Test client",
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        });
        _rootFolders
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RootFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "Movies",
                    Path = "/data/movies",
                    ContentType = "movie",
                    CreatedAt = DateTime.UtcNow,
                }
            ]);

        // Act
        var findings = await _subject.RunAsync(new() { DownloadClients = _downloadClients, }).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }

    [Test]
    public async Task RunAsync_FlagsAllDisabled_WhenIndexersAndClientsExistButAreDisabled()
    {
        // Arrange
        _rootFolders
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RootFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "Movies",
                    Path = "/data/movies",
                    ContentType = "movie",
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _indexerConnections
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = Guid.NewGuid(),
                    Name = "Test connection",
                    Enabled = false,
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _downloadClients.Add(new DownloadClient
        {
            Id = Guid.NewGuid(),
            ProviderType = "downloadclient-a",
            Name = "Test client",
            Enabled = false,
            CreatedAt = DateTime.UtcNow,
        });

        // Act
        var findings = await _subject.RunAsync(new() { DownloadClients = _downloadClients, }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(2));
            Assert.That(findings.All(f => f.Severity == DiagnosticFindingSeverity.Error), Is.True);
            Assert.That(findings.All(f => f.DiagnosticCheckId == SetupPrerequisitesDiagnostic.Id), Is.True);
            Assert.That(findings.All(f => f.Title.Contains("disabled", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public async Task RunAsync_IgnoresHealth_WhenAnIndexerIsEnabled()
    {
        // Arrange
        _rootFolders
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RootFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "Movies",
                    Path = "/data/movies",
                    ContentType = "movie",
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _downloadClients.Add(new DownloadClient
        {
            Id = Guid.NewGuid(),
            ProviderType = "downloadclient-a",
            Name = "Test client",
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        });
        _indexerConnections
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = Guid.NewGuid(),
                    Name = "Test connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Unhealthy,
                    CreatedAt = DateTime.UtcNow,
                }
            ]);

        // Act
        var findings = await _subject.RunAsync(new() { DownloadClients = _downloadClients, }).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }

    [Test]
    public async Task RunAsync_ProducesWarning_WhenDownloadClientProviderTypeDuplicated()
    {
        // Arrange
        _rootFolders
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RootFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "Movies",
                    Path = "/data/movies",
                    ContentType = "movie",
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _downloadClients.Add(new DownloadClient
        {
            Id = Guid.NewGuid(),
            ProviderType = "downloadclient-a",
            Name = "Test client",
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        });
        _indexerConnections
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = Guid.NewGuid(),
                    Name = "Test connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _downloadClientIntegrations.Add(Mock.Of<IDownloadClientIntegration>(i => i.ProviderType == "downloadclient-a"));
        _downloadClientIntegrations.Add(Mock.Of<IDownloadClientIntegration>(i => i.ProviderType == "DOWNLOADCLIENT-A"));

        // Act
        var findings = await _subject.RunAsync(new() { DownloadClients = _downloadClients, }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].DiagnosticCheckId, Is.EqualTo(SetupPrerequisitesDiagnostic.Id));
            Assert.That(findings[0].Severity, Is.EqualTo(DiagnosticFindingSeverity.Warning));
            Assert.That(
                findings[0].Title,
                Is.EqualTo("Multiple download client integrations use \"downloadclient-a\""));
            Assert.That(findings[0].Detail, Does.Contain("\"downloadclient-a\""));
            Assert.That(findings[0].Detail, Does.Contain("ignore"));
        }
    }
}
