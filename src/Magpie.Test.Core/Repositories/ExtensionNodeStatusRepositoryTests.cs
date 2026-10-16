using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ExtensionNodeStatusRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IExtensionNodeStatusRepository _subject = null!;

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
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);
        _subject = new ExtensionNodeStatusRepository(_context, NullLogger<ExtensionNodeStatusRepository>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsEveryNode_WhenNodesHaveStatuses()
    {
        // Arrange
        _context.ExtensionNodeStatuses.AddRange(
            new ExtensionNodeStatus
            {
                NodeId = "node-2",
                AppliedGeneration = 1,
                ExtensionHealth = ExtensionNodeHealth.StaleRequiresRestart,
                LastSeenAt = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc),
                Message = "Extension changes require a restart."
            },
            new ExtensionNodeStatus
            {
                NodeId = "node-1",
                AppliedGeneration = 2,
                ExtensionHealth = ExtensionNodeHealth.CurrentHealthy,
                SchedulerEligible = true,
                LastSeenAt = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var statuses = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(statuses.Select(status => status.NodeId), Is.EquivalentTo(["node-1", "node-2"]));
    }
}
