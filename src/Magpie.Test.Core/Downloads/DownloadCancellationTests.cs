using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Downloads;

[TestFixture]
public sealed class DownloadCancellationTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IDownloadRepository _downloads = null!;
    private Mock<IDownloadClientIntegration> _integration = null!;
    private Mock<IDownloadClientRepository> _clients = null!;
    private IDownloadService _subject = null!;
    private Download _download = null!;
    private DownloadClient _client = null!;
    private DateTime _now;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true).Options);
        _now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var time = new FakeTimeProvider(new DateTimeOffset(_now));
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var audit = new AuditRecordRepository(_context, Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance, transactions, Mock.Of<IJobExecutionScopeFactory>(), time);
        _downloads = new DownloadRepository(_context, audit, transactions, Mock.Of<IEventBus>(),
            NullLogger<DownloadRepository>.Instance, Mock.Of<IDataProtectionService>(), time);
        _client = new DownloadClient { Name = "Client", ProviderType = "test", Enabled = true };
        _context.DownloadClients.Add(_client);
        _download = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "transfer",
            Title = "Release",
            Status = DownloadStatus.Downloading,
            OutputPath = "/library/shared-file.mkv",
            Progress = 45,
            HeldByPolicy = true,
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "torrent", Value = "release-hash" } }
        };
        _context.Downloads.Add(_download);
        _context.SaveChanges();
        _clients = new Mock<IDownloadClientRepository>();
        _clients.Setup(repository => repository.GetByIdAsync(_client.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_client);
        _integration = new Mock<IDownloadClientIntegration>();
        _integration.SetupGet(integration => integration.ProviderType).Returns("test");
        _integration.Setup(integration => integration.RemoveDownloadAsync(
                "transfer", false, _client.Settings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _subject = new DownloadService([_integration.Object], _clients.Object, _downloads,
            Mock.Of<IJobManager>(), transactions, time, NullLogger<DownloadService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CancelWithOutcomeAsync_KeepsHistoryWithoutTaint_WhenClientRemovesOrAlreadyLostTransfer(
        bool removed)
    {
        // Arrange
        _integration.Setup(integration => integration.RemoveDownloadAsync(
                "transfer", false, _client.Settings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(removed);

        // Act
        var outcome = await _subject.CancelWithOutcomeAsync(_download.Id).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);
        var repeated = await _subject.CancelWithOutcomeAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(DownloadCancellationOutcome.Cancelled));
            Assert.That(repeated, Is.EqualTo(DownloadCancellationOutcome.AlreadyCancelled));
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(stored.ClientRemovedAt, Is.EqualTo(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(stored.HeldByPolicy, Is.False);
            Assert.That(stored.FailureReason, Is.Null);
            Assert.That(stored.FailedAt, Is.Null);
            Assert.That(stored.OutputPath, Is.EqualTo("/library/shared-file.mkv"));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "transfer", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task CancelWithOutcomeAsync_LeavesAttemptRecoverable_WhenClientFailsThenAcceptsRetry()
    {
        // Arrange
        _integration.SetupSequence(integration => integration.RemoveDownloadAsync(
                "transfer", false, _client.Settings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Client unavailable"))
            .ReturnsAsync(true);

        // Act
        var failure = await _subject.CancelWithOutcomeAsync(_download.Id).ConfigureAwait(false);
        var unchanged = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);
        var retry = await _subject.CancelWithOutcomeAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.EqualTo(DownloadCancellationOutcome.ClientError));
            Assert.That(unchanged!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(unchanged.FailureReason, Is.Null);
            Assert.That(unchanged.ClientRemovedAt, Is.Null);
            Assert.That(unchanged.HeldByPolicy, Is.True);
            Assert.That(retry, Is.EqualTo(DownloadCancellationOutcome.Cancelled));
        }
    }

    [Test]
    public async Task CancelAsync_RemovesTransfer_WhenAnOlderCancellationOnlyChangedLocalState()
    {
        // Arrange
        _download.Status = DownloadStatus.Cancelled;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var cancelled = await _subject.CancelAsync(_download.Id).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancelled, Is.True);
            Assert.That(stored!.ClientRemovedAt, Is.EqualTo(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc)));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "transfer", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(DownloadStatus.Imported)]
    [TestCase(DownloadStatus.Importing)]
    [TestCase(DownloadStatus.Failed)]
    public async Task CancelWithOutcomeAsync_DoesNotContactClient_WhenImportOrFailureAlreadyWon(DownloadStatus status)
    {
        // Arrange
        _download.Status = status;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var outcome = await _subject.CancelWithOutcomeAsync(_download.Id).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(DownloadCancellationOutcome.NotCancellable));
            Assert.That(stored!.Status, Is.EqualTo(status));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task CancelWithOutcomeAsync_ReportsUnavailableAndKeepsStatus_WhenClientConfigurationIsMissing()
    {
        // Arrange
        _clients.Setup(repository => repository.GetByIdAsync(_client.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadClient?)null);

        // Act
        var outcome = await _subject.CancelWithOutcomeAsync(_download.Id).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(DownloadCancellationOutcome.ClientUnavailable));
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(stored.FailureReason, Is.Null);
        }
    }

    [Test]
    public async Task CancelWithOutcomeAsync_ReportsNotFound_WhenRecordDisappears()
    {
        // Arrange
        _context.Downloads.Remove(_download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var outcome = await _subject.CancelWithOutcomeAsync(_download.Id).ConfigureAwait(false);

        // Assert
        Assert.That(outcome, Is.EqualTo(DownloadCancellationOutcome.NotFound));
    }

    [Test]
    public async Task CancelAsync_PreventsStaleRefreshAndImport_WhenTransferCompletesDuringCancellation()
    {
        // Arrange
        _integration.Setup(integration => integration.RemoveDownloadAsync(
                "transfer", false, _client.Settings, It.IsAny<CancellationToken>()))
            .Callback(() => Assert.That(_context.Database.CurrentTransaction, Is.Not.Null))
            .ReturnsAsync(true);
        var completed = new DownloadInfo
        {
            ExternalId = "transfer",
            Status = DownloadStatus.Downloaded,
            ProgressPercent = 100
        };

        // Act
        await _subject.CancelAsync(_download.Id).ConfigureAwait(false);
        await _subject.ApplyRefreshedClientStateAsync(_download, completed, null).ConfigureAwait(false);
        var importing = await _downloads.MarkImportingAsync(_download.Id, _now, null).ConfigureAwait(false);
        var failed = await _downloads.MarkFailedAsync(_download.Id, DownloadFailureReason.ImportFailed,
            "Stale import", "ImportFailed", "Stale import", _now, null, null).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(importing, Is.False);
            Assert.That(failed, Is.False);
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(stored.FailureReason, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BulkCancelAsync_CountsOnlyCancelledTransfers_WhenOneClientRequestFails(bool stopAlias)
    {
        // Arrange
        var second = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "second",
            Title = "Second",
            Status = DownloadStatus.Downloaded
        };
        _context.Downloads.Add(second);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _integration.Setup(integration => integration.RemoveDownloadAsync(
                "transfer", false, _client.Settings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unavailable"));
        _integration.Setup(integration => integration.RemoveDownloadAsync(
                "second", false, _client.Settings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var action = new BulkAction<DownloadQuery> { Ids = [_download.Id, second.Id, second.Id] };

        // Act
        var result = stopAlias
            ? await _subject.BulkStopAsync(action).ConfigureAwait(false)
            : await _subject.BulkCancelAsync(action).ConfigureAwait(false);
        var unchanged = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);
        var cancelled = await _downloads.GetByIdAsync(second.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Error, Is.EqualTo("Failed to cancel 1 download."));
            Assert.That(unchanged!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(cancelled!.Status, Is.EqualTo(DownloadStatus.Cancelled));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "second", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
