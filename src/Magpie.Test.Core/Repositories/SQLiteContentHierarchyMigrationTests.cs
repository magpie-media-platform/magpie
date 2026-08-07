using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Data;
using Magpie.Data.SQLite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class SQLiteContentHierarchyMigrationTests
{
    [Test]
    public async Task MigrateAsync_CreatesCurrentSchema_FromEmptyDatabase()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        var options = new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(
                connection,
                sqlite => sqlite.MigrationsAssembly(typeof(SQLiteDatabaseManagementProvider).Assembly.FullName))
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .Options;
        using var context = new MagpieDbContext(options);
        var migrator = context.Database.GetService<IMigrator>();

        // Act
        await migrator.MigrateAsync().ConfigureAwait(false);

        // Assert
        var columns = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"content_hierarchyItem\");";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                columns.Add(reader.GetString(1));
            }
        }

        var indexes = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA index_list(\"content_hierarchyItem\");";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var name = reader.GetString(1);
                if (!name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal))
                {
                    indexes.Add(name);
                }
            }
        }

        var foreignKeys = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_list(\"content_hierarchyItem\");";
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                foreignKeys.Add(
                    $"{reader.GetString(2)}:{reader.GetString(3)}:" +
                    $"{reader.GetString(4)}:{reader.GetString(6)}");
            }
        }

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                columns.Order(),
                Is.EqualTo(
                [
                    "AlignmentKey",
                    "ContentType",
                    "CreatedAt",
                    "CreatedByUserId",
                    "DateLastRefreshed",
                    "EffectiveMonitoring",
                    "EffectiveQualityTierListId",
                    "EffectiveRootFolderId",
                    "Id",
                    "LanguagePreferences",
                    "Level",
                    "MetadataContainerId",
                    "ModifiedAt",
                    "ModifiedByUserId",
                    "Monitoring",
                    "Name",
                    "Ordinal",
                    "ParentId",
                    "Path",
                    "PendingMetadataRefresh",
                    "QualityTierListId",
                    "RootFolderId",
                    "Settings",
                    "Slug"
                ]));
            Assert.That(
                indexes.Order(),
                Is.EqualTo(
                [
                    "IX_content_hierarchyItem_ContentType_Level",
                    "IX_content_hierarchyItem_ContentType_Level_Slug",
                    "IX_content_hierarchyItem_ContentType_ParentId_Level_Slug",
                    "IX_content_hierarchyItem_EffectiveMonitoring",
                    "IX_content_hierarchyItem_EffectiveQualityTierListId",
                    "IX_content_hierarchyItem_EffectiveRootFolderId",
                    "IX_content_hierarchyItem_MetadataContainerId",
                    "IX_content_hierarchyItem_ParentId",
                    "IX_content_hierarchyItem_QualityTierListId",
                    "IX_content_hierarchyItem_RootFolderId"
                ]));
            Assert.That(
                foreignKeys.Order(),
                Is.EqualTo(
                [
                    "content_hierarchyItem:ParentId:Id:RESTRICT",
                    "metadata_container:MetadataContainerId:Id:RESTRICT",
                    "quality_tierList:QualityTierListId:Id:SET NULL",
                    "system_rootFolder:RootFolderId:Id:RESTRICT"
                ]));
            Assert.That(columns, Does.Not.Contain("MetadataRefreshAttempts"));
            Assert.That(
                appliedMigrations,
                Is.EqualTo(["20260509010401_Initial", "20260805235539_sdk_redesign"]));
        }
    }
}
