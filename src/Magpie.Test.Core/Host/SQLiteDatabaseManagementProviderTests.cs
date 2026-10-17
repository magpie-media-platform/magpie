using System;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class SQLiteDatabaseManagementProviderTests
{
    [Test]
    public async Task GetUnknownMigrationsAsync_ReturnsOnlyTheNewerMigrations_WhenTheHistoryHasSquashedMigrations()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(connection).Options);
        var history = context.GetService<IHistoryRepository>();
        await history.CreateIfNotExistsAsync().ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow("20260820185956_audit_job_execution_id", "10.0.0")))
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow("29990101000000_newer_node", "10.0.0")))
            .ConfigureAwait(false);
        var subject = new SQLiteDatabaseManagementProvider(
            context,
            new MagpieHostEnvironment(
                "node-a",
                IsClustered: false,
                IsSupervised: false,
                SystemRoot: string.Empty,
                ExtensionsRoot: string.Empty,
                ConfigRoot: string.Empty,
                DataRoot: string.Empty,
                StartedAt: new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc)),
            Mock.Of<ISettingsRepository>(),
            TimeProvider.System,
            NullLogger<SQLiteDatabaseManagementProvider>.Instance);

        // Act
        var unknown = await subject.GetUnknownMigrationsAsync().ConfigureAwait(false);

        // Assert
        Assert.That(unknown, Is.EqualTo(["29990101000000_newer_node"]));
    }
}
