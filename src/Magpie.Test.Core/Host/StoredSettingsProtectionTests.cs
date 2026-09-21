using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Security;
using Magpie.App.Hosting;
using Magpie.Core.Security;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class StoredSettingsProtectionTests
{
    private MagpieDbContext _context = null!;
    private string _dataRoot = null!;
    private IDataProtectionService _protector = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _dataRoot = Path.Combine(Path.GetTempPath(), "magpie-stored-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        _protector = new DataProtectionService(new DefaultDataProtectionProvider(
            new MagpieHostEnvironment(
                NodeId: "test-node",
                IsClustered: false,
                IsSupervised: false,
                SystemRoot: _dataRoot,
                ExtensionsRoot: _dataRoot,
                ConfigRoot: _dataRoot,
                DataRoot: _dataRoot,
                StartedAt: DateTime.UtcNow),
            NullLogger<DefaultDataProtectionProvider>.Instance));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        Directory.Delete(_dataRoot, recursive: true);
    }

    [Test]
    public async Task ProtectStoredSettingsAsync_ProtectsTheSettings_WhenTheStoredRowIsPlaintext()
    {
        // Arrange
        _context.MediaServers.Add(new MediaServer
        {
            Name = "Server",
            ProviderType = "jellyfin",
            CreatedAt = DateTime.UtcNow,
            Settings = new Dictionary<string, string> { ["apiKey"] = "server-secret" }
        });
        _context.NotificationRules.Add(new NotificationRule
        {
            Name = "Rule",
            ProviderType = "webhook",
            CreatedAt = DateTime.UtcNow,
            Settings = new Dictionary<string, string> { ["apiKey"] = "rule-secret" }
        });
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await DatabaseStartupService
            .ProtectStoredSettingsAsync(_context, _protector, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        var server = await _context.MediaServers.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var rule = await _context.NotificationRules.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(server.Settings.Keys, Is.EqualTo(["__magpieSecret"]));
            Assert.That(_protector.Unprotect(server.Settings)["apiKey"], Is.EqualTo("server-secret"));
            Assert.That(rule.Settings.Keys, Is.EqualTo(["__magpieSecret"]));
            Assert.That(_protector.Unprotect(rule.Settings)["apiKey"], Is.EqualTo("rule-secret"));
        }
    }

    [Test]
    public async Task ProtectStoredSettingsAsync_LeavesTheSettingsUnchanged_WhenTheStoredRowIsAlreadyProtected()
    {
        // Arrange
        _context.MediaServers.Add(new MediaServer
        {
            Name = "Server",
            ProviderType = "jellyfin",
            CreatedAt = DateTime.UtcNow,
            Settings = _protector.Protect(new Dictionary<string, string> { ["apiKey"] = "server-secret" })
        });
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var envelope = (await _context.MediaServers.AsNoTracking().SingleAsync().ConfigureAwait(false))
            .Settings["__magpieSecret"];

        // Act
        await DatabaseStartupService
            .ProtectStoredSettingsAsync(_context, _protector, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        var server = await _context.MediaServers.AsNoTracking().SingleAsync().ConfigureAwait(false);
        Assert.That(server.Settings["__magpieSecret"], Is.EqualTo(envelope));
    }

    [Test]
    public async Task ProtectStoredSettingsAsync_LeavesTheSettingsEmpty_WhenTheStoredRowHasNoSettings()
    {
        // Arrange
        _context.NotificationRules.Add(new NotificationRule
        {
            Name = "Rule",
            ProviderType = "webhook",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await DatabaseStartupService
            .ProtectStoredSettingsAsync(_context, _protector, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        var rule = await _context.NotificationRules.AsNoTracking().SingleAsync().ConfigureAwait(false);
        Assert.That(rule.Settings, Is.Empty);
    }
}
