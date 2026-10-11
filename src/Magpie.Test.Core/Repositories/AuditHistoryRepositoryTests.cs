using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class AuditHistoryRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid ContentId = new("00000000-0000-0000-0000-0000000000c1");

    private static readonly Guid DownloadId = new("00000000-0000-0000-0000-0000000000d1");

    private static readonly Guid CancelledExecutionId = new("00000000-0000-0000-0000-0000000000e1");

    private static readonly Guid SignInCorrelationId = new("00000000-0000-0000-0000-0000000000f1");

    private static readonly Guid SystemUserId = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid GrabCorrelationId = new("00000000-0000-0000-0000-0000000000f2");

    private static readonly Guid GrabbedHierarchyItemId = new("00000000-0000-0000-0000-0000000000c2");

    private static readonly Guid GrabbedContentItemId = new("00000000-0000-0000-0000-0000000000c3");

    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IAuditHistoryRepository _subject = null!;
    private Guid _userId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
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
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);

        var user = new User
        {
            Username = "magpie-admin",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = Now
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        _userId = user.Id;

        _context.AuditRecords.AddRange(
            new AuditRecord
            {
                Category = AuditCategories.Security,
                Operation = "SignIn",
                Detail = "Signed in with a password",
                UserId = _userId,
                CorrelationId = SignInCorrelationId,
                Timestamp = Now.AddMinutes(-1)
            },
            new AuditRecord
            {
                Category = AuditCategories.Security,
                Operation = "SignInFailed",
                Detail = "Wrong password",
                EntityId = _userId.ToString("D"),
                EntityType = nameof(User),
                Timestamp = Now.AddMinutes(-2)
            },
            new AuditRecord
            {
                Category = AuditCategories.Content,
                Operation = "Update",
                Detail = "Updated The Matrix",
                EntityId = ContentId.ToString("D"),
                EntityType = nameof(ContentHierarchyItem),
                UserId = _userId,
                Timestamp = Now.AddMinutes(-4)
            },
            new AuditRecord
            {
                Category = AuditCategories.Jobs,
                Operation = "Cancel",
                Detail = "Cancelled the release search",
                UserId = _userId,
                EntityId = CancelledExecutionId.ToString("D"),
                EntityType = nameof(JobExecution),
                JobExecutionId = CancelledExecutionId,
                Timestamp = Now.AddMinutes(-5)
            },
            new AuditRecord
            {
                Category = AuditCategories.System,
                Operation = "BackupDeleted",
                Detail = "Database backup deleted.",
                Timestamp = Now.AddMinutes(-7)
            });
        _context.JobExecutions.AddRange(
            new JobExecution
            {
                Id = CancelledExecutionId,
                JobType = "ReleaseSearchScan",
                Status = JobExecutionStatus.Completed,
                StatusMessage = "Searched 3 items",
                CorrelationId = SignInCorrelationId,
                CreatedAt = Now.AddMinutes(-4),
                CompletedAt = Now.AddMinutes(-3)
            },
            new JobExecution
            {
                JobType = "DownloadImportItem",
                Status = JobExecutionStatus.Failed,
                StatusMessage = "The output path does not exist.",
                Parameters = new() { ["downloadId"] = DownloadId.ToString("D") },
                CreatedByUserId = _userId,
                CreatedAt = Now.AddMinutes(-7),
                CompletedAt = Now.AddMinutes(-6)
            },
            new JobExecution
            {
                JobType = "DownloadImportItem",
                Status = JobExecutionStatus.Failed,
                StatusMessage = "The download is missing.",
                TriggerContext = new() { ["event.context.download.id"] = DownloadId.ToString("D") },
                CreatedAt = Now.AddMinutes(-9),
                CompletedAt = Now.AddMinutes(-8)
            },
            new JobExecution
            {
                JobType = "RetentionCleanup",
                Status = JobExecutionStatus.Running,
                CreatedByUserId = SystemUserId,
                CreatedAt = Now.AddMinutes(-10),
                StartedAt = Now.AddMinutes(-9)
            });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        _subject = new AuditHistoryRepository(
            _context,
            new JobRegistry(
                [new(DownloadImportItemJob.Spec, typeof(DownloadImportItemJob))],
                NullLogger<JobRegistry>.Instance),
            NullLogger<AuditHistoryRepository>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [TestCase(
        SortDirection.Descending,
        new[]
        {
            "SignIn",
            "SignInFailed",
            "ReleaseSearchScan",
            "Update",
            "Cancel",
            "DownloadImportItem",
            "BackupDeleted",
            "DownloadImportItem",
            "RetentionCleanup"
        })]
    [TestCase(
        SortDirection.Ascending,
        new[]
        {
            "RetentionCleanup",
            "DownloadImportItem",
            "BackupDeleted",
            "DownloadImportItem",
            "Cancel",
            "Update",
            "ReleaseSearchScan",
            "SignInFailed",
            "SignIn"
        })]
    public async Task QueryAsync_MergesBothSourcesInTimestampOrder_WhenNoFilterIsGiven(
        SortDirection direction,
        string[] expectedLabels)
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, Direction = direction };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(expectedLabels));
            Assert.That(page.TotalItems, Is.EqualTo(9));
        }
    }

    [TestCase(new[] { "Security" }, new[] { "SignIn", "SignInFailed" })]
    [TestCase(
        new[] { "Jobs" },
        new[] { "ReleaseSearchScan", "Cancel", "DownloadImportItem", "DownloadImportItem", "RetentionCleanup" })]
    [TestCase(new[] { "Content", "System" }, new[] { "Update", "BackupDeleted" })]
    public async Task QueryAsync_ReturnsTheEntriesOfTheCategories_WhenCategoriesAreGiven(
        string[] categories,
        string[] expectedLabels)
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, Categories = categories };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(expectedLabels));
            Assert.That(page.TotalItems, Is.EqualTo(expectedLabels.Length));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsTheEntriesOfTheUser_WhenUserIdIsGiven()
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, UserId = _userId };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            page.Items.Select(entry => entry.Label),
            Is.EqualTo(["SignIn", "Update", "Cancel", "DownloadImportItem"]));
    }

    [TestCase(
        false,
        new[] { "SignInFailed", "ReleaseSearchScan", "BackupDeleted", "DownloadImportItem", "RetentionCleanup" })]
    [TestCase(true, new[] { "SignIn", "Update", "Cancel", "DownloadImportItem" })]
    public async Task QueryAsync_CountsTheSystemUserAsNoUser_WhenHasUserIsGiven(bool hasUser, string[] expectedLabels)
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, HasUser = hasUser };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(expectedLabels));
    }

    [TestCase("PASSWORD", new[] { "SignIn", "SignInFailed" })]
    [TestCase("Sign In Failed", new[] { "SignInFailed" })]
    [TestCase("release search", new[] { "ReleaseSearchScan", "Cancel" })]
    [TestCase("backup deleted", new[] { "BackupDeleted" })]
    [TestCase("backupdel", new[] { "BackupDeleted" })]
    [TestCase("import", new[] { "DownloadImportItem", "DownloadImportItem" })]
    [TestCase(" output path ", new[] { "DownloadImportItem" })]
    public async Task QueryAsync_ReturnsTheEntriesThatContainTheText_WhenSearchIsGiven(
        string search,
        string[] expectedLabels)
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, Search = search };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(expectedLabels));
    }

    [TestCase("failed", new[] { "DownloadImportItem", "DownloadImportItem" })]
    [TestCase("Paused", new string[0])]
    public async Task QueryAsync_ReturnsOnlyJobExecutions_WhenStatusIsGiven(string status, string[] expectedLabels)
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, Status = status };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(expectedLabels));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheAuditsOfTheExecution_WhenJobExecutionIdIsGiven()
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            JobExecutionId = CancelledExecutionId
        };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(["Cancel"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheContentAuditsAndTheReferencingJobs_WhenTheContentIsGiven()
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, ContentId = ContentId };

        // Act
        var page = await _subject.QueryAsync(
            query,
            ["00000000-0000-0000-0000-0000000000C1", "00000000-0000-0000-0000-0000000000D1"]).ConfigureAwait(false);

        // Assert
        var entries = page.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                entries.Select(entry => entry.Label),
                Is.EqualTo(["Update", "DownloadImportItem", "DownloadImportItem"]));
            Assert.That(
                entries.Select(entry => entry.EntityId),
                Is.EqualTo(
                [
                    "00000000-0000-0000-0000-0000000000c1",
                    "00000000-0000-0000-0000-0000000000d1",
                    "00000000-0000-0000-0000-0000000000d1"
                ]));
            Assert.That(entries.Select(entry => entry.Category), Is.EqualTo(["Content", "Jobs", "Jobs"]));
        }
    }

    [TestCase(SortDirection.Descending, 2, new[] { "ReleaseSearchScan", "Update" })]
    [TestCase(SortDirection.Descending, 3, new[] { "Cancel", "DownloadImportItem" })]
    [TestCase(SortDirection.Descending, 5, new[] { "RetentionCleanup" })]
    [TestCase(SortDirection.Descending, 6, new string[0])]
    [TestCase(SortDirection.Ascending, 4, new[] { "ReleaseSearchScan", "SignInFailed" })]
    public async Task QueryAsync_ReturnsTheRowsOfTheDeepPage_WhenThePageIsBeyondTheFirst(
        SortDirection direction,
        int pageNumber,
        string[] expectedLabels)
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = pageNumber, PageSize = 2 },
            Direction = direction
        };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(expectedLabels));
            Assert.That(page.TotalItems, Is.EqualTo(9));
        }
    }

    [TestCase(
        """
        SELECT "s"."Id", 1 AS "Kind", "s"."Timestamp"
        FROM "system_audit" AS "s"
        WHERE "s"."Category" IN ('Security', 'Jobs')
        ORDER BY "s"."Timestamp" DESC, "s"."Id" DESC
        LIMIT 25 OFFSET 1000
        """,
        "SCAN s USING COVERING INDEX IX_system_audit_Timestamp_Id_Category_UserId")]
    [TestCase(
        """
        SELECT "s"."Id", 1 AS "Kind", "s"."Timestamp"
        FROM "system_audit" AS "s"
        WHERE "s"."UserId" IS NULL OR "s"."UserId" = '11111111-1111-1111-1111-111111111111'
        ORDER BY "s"."Timestamp" DESC, "s"."Id" DESC
        LIMIT 25 OFFSET 1000
        """,
        "SCAN s USING COVERING INDEX IX_system_audit_Timestamp_Id_Category_UserId")]
    [TestCase(
        """
        SELECT "j"."Id", 0 AS "Kind", "j"."CompletedAt" AS "Timestamp"
        FROM "job_execution" AS "j"
        WHERE "j"."CreatedByUserId" = '11111111-1111-1111-1111-111111111111' AND "j"."CompletedAt" IS NOT NULL
        ORDER BY "j"."CompletedAt" DESC, "j"."Id" DESC
        LIMIT 25 OFFSET 1000
        """,
        "SEARCH j USING COVERING INDEX IX_job_execution_CompletedAt_Id_CreatedByUserId (CompletedAt>?)")]
    public async Task QueryAsync_WalksTheTimeIndex_WhenTheFilterIsACategoryOrAUser(string sql, string expectedPlan)
    {
        // Arrange
        using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;

        // Act
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var plan = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            plan.Add(reader.GetString(3));
        }

        // Assert
        Assert.That(plan, Is.EqualTo([expectedPlan]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheEntriesInTheRange_WhenFromAndToAreGiven()
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            From = new DateTime(2026, 10, 7, 11, 54, 30, DateTimeKind.Utc),
            To = new DateTime(2026, 10, 7, 11, 57, 30, DateTimeKind.Utc)
        };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(["ReleaseSearchScan", "Update", "Cancel"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheUnfinishedJobExecution_WhenToIsBeforeEveryOtherEntry()
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            To = new DateTime(2026, 10, 7, 11, 51, 30, DateTimeKind.Utc)
        };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(["RetentionCleanup"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyJobExecutions_WhenKindIsJobExecution()
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            Kind = AuditHistoryEntryKind.JobExecution
        };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            page.Items.Select(entry => entry.Label),
            Is.EqualTo(["ReleaseSearchScan", "DownloadImportItem", "DownloadImportItem", "RetentionCleanup"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheCorrelatedEntries_WhenCorrelationIdIsGiven()
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            CorrelationId = SignInCorrelationId
        };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(["SignIn", "ReleaseSearchScan"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheTargetOfEachEntry_WhenNoFilterIsGiven()
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 } };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                page.Items.Select(entry => entry.EntityType),
                Is.EqualTo(
                [
                    null, "User", null, "ContentHierarchyItem", "JobExecution", "Download", null, "Download", null
                ]));
            Assert.That(
                page.Items.Select(entry => entry.EntityName),
                Is.EqualTo([null, "magpie-admin", null, null, "ReleaseSearchScan", null, null, null, null]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsTheContentOfTheContentItem_WhenTheTargetIsAContentItem()
    {
        // Arrange
        var metadataContainer = new MetadataContainer { CreatedAt = Now };
        _context.MetadataContainers.Add(metadataContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new()
        {
            Id = GrabbedHierarchyItemId,
            MetadataContainerId = metadataContainer.Id,
            ContentType = "movie",
            Level = "movie",
            Slug = "the-matrix",
            Name = "The Matrix",
            CreatedAt = Now
        });
        _context.ContentItems.Add(new()
        {
            Id = GrabbedContentItemId,
            ContentHierarchyItemId = GrabbedHierarchyItemId,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = Now
        });
        _context.AuditRecords.Add(new()
        {
            Category = AuditCategories.Content,
            Operation = "ReleaseGrabbed",
            EntityId = GrabbedContentItemId.ToString("D"),
            EntityType = nameof(ContentItem),
            CorrelationId = GrabCorrelationId,
            Timestamp = Now
        });
        _context.JobExecutions.Add(new()
        {
            JobType = "ReleaseAutoGrabItem",
            Status = JobExecutionStatus.Completed,
            Parameters = new() { ["contentItemId"] = GrabbedContentItemId.ToString("D") },
            CorrelationId = GrabCorrelationId,
            CreatedAt = Now.AddMinutes(-2),
            CompletedAt = Now.AddMinutes(-1)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            CorrelationId = GrabCorrelationId
        };

        // Act
        var page = await _subject.QueryAsync(query, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                page.Items.Select(entry => entry.EntityType),
                Is.EqualTo(["ContentHierarchyItem", "ContentHierarchyItem"]));
            Assert.That(
                page.Items.Select(entry => entry.EntityId),
                Is.EqualTo(["00000000-0000-0000-0000-0000000000c2", "00000000-0000-0000-0000-0000000000c2"]));
            Assert.That(page.Items.Select(entry => entry.EntityName), Is.EqualTo(["The Matrix", "The Matrix"]));
        }
    }
}
