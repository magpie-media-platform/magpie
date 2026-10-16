using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Config;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Releases;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ReleaseSearchEnqueueTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ServiceProvider _services = null!;
    private EventDispatchBackgroundService _dispatcher = null!;
    private ReleaseSearchScanJob _scan = null!;
    private IJobManager _manager = null!;
    private IJobExecutionRepository _executions = null!;
    private IScheduleTriggerRepository _schedules = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchy = null!;
    private IEventTriggerRepository _triggers = null!;
    private INotificationRuleRepository _rules = null!;
    private IUserRepository _users = null!;
    private IReadOnlyList<Guid> _roots = null!;
    private ITransactionManager _transactions = null!;
    private readonly Mock<IJobExecutionScopeFactory> _scopeMock = new();
    private IJobExecutionScopeFactory _scopes = null!;

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
        _scopes = _scopeMock.Object;
        var connectionString = $"Data Source=search-enqueue-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        _template.BackupDatabase(connection);
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, connectionString, [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _transactions = transactions;
        _executions = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(_context, Mock.Of<IEventBus>(), NullLogger<AuditRecordRepository>.Instance,
                transactions, _scopes, TimeProvider.System),
            transactions,
            Mock.Of<IEventBus>(),
            NullLogger<JobExecutionRepository>.Instance,
            TimeProvider.System,
            new JobRegistry([], new JobRuntimeOptions(), NullLogger<JobRegistry>.Instance));
        var registry = new Mock<IJobRegistry>();
        registry.Setup(value => value.ListJobSpecs()).Returns([ReleaseSearchItemJob.Spec, ReleaseSearchScanJob.Spec]);
        _schedules = new ScheduleTriggerRepository(_context,
            new AuditRecordRepository(_context, Mock.Of<IEventBus>(), NullLogger<AuditRecordRepository>.Instance,
                transactions, _scopes, TimeProvider.System),
            transactions, Mock.Of<IEventBus>(), NullLogger<ScheduleTriggerRepository>.Instance, TimeProvider.System);
        var triggers = new Mock<IEventTriggerRepository> { CallBase = true };
        var eventTriggers = DefaultJobTriggerCatalog.EventTriggers.ToArray();
        _context.EventTriggers.AddRange(eventTriggers);
        _context.SaveChanges();
        triggers.Setup(value => value.QueryAsync(It.IsAny<EventTriggerQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = eventTriggers });
        _triggers = triggers.Object;
        var rules = new Mock<INotificationRuleRepository> { CallBase = true };
        rules.Setup(value => value.QueryAsync(It.IsAny<NotificationRuleQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        _rules = rules.Object;
        var users = new Mock<IUserRepository> { CallBase = true };
        users.Setup(value => value.QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        _users = users.Object;
        _scopeMock.SetupGet(value => value.CurrentExecutionId).Returns((Guid?)null);
        _manager = new JobManager(Mock.Of<IJobRuntime>(), registry.Object, _executions, _schedules,
            _triggers, Mock.Of<IJobConcurrencyScheduler>(), _scopes, TimeProvider.System,
            NullLogger<JobManager>.Instance, transactions);
        _roots = [];
        _hierarchy = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        _hierarchy.Setup(value => value.QueryIdsAsync(It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _roots);
        _hierarchy.Setup(value => value.GetAncestryAsync(It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _roots.ToDictionary(id => id,
                id => new ContentHierarchyItemAncestry { Id = id, EffectiveMonitoring = MonitoringMode.Monitored }));
        _scan = new ReleaseSearchScanJob(_manager, _hierarchy.Object, TimeProvider.System,
            NullLogger<ReleaseSearchScanJob>.Instance, Mock.Of<IProgressReporter>());
        _services = new ServiceCollection().BuildServiceProvider();
        var migration = new DatabaseMigrationState();
        migration.Complete();
        _dispatcher = new EventDispatchBackgroundService(Mock.Of<IEventBus>(),
            _services.GetRequiredService<IServiceScopeFactory>(), NullLogger<EventDispatchBackgroundService>.Instance,
            new MagpieHostEnvironment("search-enqueue-test", false, false, "", "", "", "", DateTime.UtcNow),
            TimeProvider.System, migration);
    }

    [TearDown]
    public void TearDown()
    {
        _dispatcher.Dispose();
        _services.Dispose();
        _context.Dispose();
    }

    [TestCase(0, 1, 1)]
    [TestCase(50, 3, 53)]
    public async Task DispatchAsync_QueuesEverySearch_WhenMetadataRefreshesArriveInABurst(
        int alreadyActive, int incoming, int expectedActive)
    {
        // Arrange
        _context.ScheduleTriggers.Add(new ScheduleTrigger
        {
            JobType = "ReleaseSearchScan",
            Scope = JobScope.Content,
            Name = "search",
            Schedule = "*/30 * * * *",
            Enabled = true,
            Parameters = new()
        });
        _context.JobExecutions.AddRange(Enumerable.Range(0, alreadyActive).Select(_ => new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            CreatedAt = DateTime.UtcNow.AddHours(-3)
        }));
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var items = Enumerable.Range(0, incoming).Select(_ => Guid.NewGuid()).ToArray();

        // Act
        foreach (var id in items)
        {
            await ((Task)typeof(EventDispatchBackgroundService)
                .GetMethod("DispatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_dispatcher,
                [
                    new MagpieEvent(Guid.NewGuid(), "metadata.refreshed",
                        new Dictionary<string, string>
                        {
                            [MagpieEvents.Keys.ContentHierarchyItemId] = id.ToString("D")
                        },
                        DateTime.UtcNow, DateTime.UtcNow, null),
                    _triggers, _schedules, _rules, _manager, _users, Mock.Of<IDownloadScopeRepository>(),
                    Mock.Of<IContentRepository>(), _hierarchy.Object, _executions, _scopes,
                    Array.Empty<INotificationProvider>(), CancellationToken.None
                ])!).ConfigureAwait(false);
        }

        // Assert
        var searches = await _context.JobExecutions.AsNoTracking()
            .Where(value => value.JobType == "ReleaseSearchItem" && value.Status == JobExecutionStatus.Queued)
            .ToListAsync().ConfigureAwait(false);
        var itemIds = items.Select(id => id.ToString("D")).ToHashSet();
        var itemSearches = searches.Where(value =>
            value.Parameters.TryGetValue("contentHierarchyItemId", out var raw) && itemIds.Contains(raw)).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(searches, Has.Count.EqualTo(expectedActive));
            Assert.That(itemSearches.Select(value => value.Parameters["contentHierarchyItemId"]),
                Is.EquivalentTo(itemIds));
            Assert.That(itemSearches.Select(value => value.NotBeforeAt), Is.All.Null);
            Assert.That(await _context.AuditRecords.AsNoTracking()
                .Where(value => value.Operation != "Create").Select(value => value.Operation)
                .ToListAsync().ConfigureAwait(false), Is.Empty);
        }
    }

    [TestCase(50, 53)]
    [TestCase(51, 54)]
    [TestCase(2001, 2004)]
    public async Task ExecuteAsync_QueuesEveryRoot_WhenOtherSearchesAreAlreadyQueued(
        int alreadyActive,
        int expectedActive)
    {
        // Arrange
        _context.JobExecutions.AddRange(Enumerable.Range(0, alreadyActive).Select(_ => new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            CreatedAt = DateTime.UtcNow.AddHours(-3)
        }));
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _roots = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        // Act
        await _scan.ExecuteAsync(new JobContext(Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
            new Dictionary<string, string>(), null)).ConfigureAwait(false);

        // Assert
        Assert.That(await _context.JobExecutions.AsNoTracking()
            .CountAsync(value => value.JobType == "ReleaseSearchItem" && value.Status == JobExecutionStatus.Queued)
            .ConfigureAwait(false), Is.EqualTo(expectedActive));
    }

    [TestCase("event")]
    [TestCase("scan")]
    public async Task DispatchAsync_KeepsOneQueuedSearchPerItem_WhenTheItemAlreadyHasAQueuedSearch(string first)
    {
        // Arrange
        var item = Guid.NewGuid();
        _roots = [item];
        if (first == "event")
        {
            await ((Task)typeof(EventDispatchBackgroundService)
                .GetMethod("DispatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_dispatcher,
                [
                    new MagpieEvent(Guid.NewGuid(), "metadata.refreshed",
                        new Dictionary<string, string>
                        {
                            [MagpieEvents.Keys.ContentHierarchyItemId] = item.ToString("D")
                        },
                        DateTime.UtcNow, DateTime.UtcNow, null),
                    _triggers, _schedules, _rules, _manager, _users, Mock.Of<IDownloadScopeRepository>(),
                    Mock.Of<IContentRepository>(), _hierarchy.Object, _executions, _scopes,
                    Array.Empty<INotificationProvider>(), CancellationToken.None
                ])!).ConfigureAwait(false);
        }
        else
        {
            await _scan.ExecuteAsync(new JobContext(Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
                new Dictionary<string, string>(), null)).ConfigureAwait(false);
        }

        // Act
        await ((Task)typeof(EventDispatchBackgroundService)
            .GetMethod("DispatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_dispatcher,
            [
                new MagpieEvent(Guid.NewGuid(), "metadata.refreshed",
                    new Dictionary<string, string>
                    {
                        [MagpieEvents.Keys.ContentHierarchyItemId] = item.ToString("D")
                    },
                    DateTime.UtcNow, DateTime.UtcNow, null),
                _triggers, _schedules, _rules, _manager, _users, Mock.Of<IDownloadScopeRepository>(),
                Mock.Of<IContentRepository>(), _hierarchy.Object, _executions, _scopes,
                Array.Empty<INotificationProvider>(), CancellationToken.None
            ])!).ConfigureAwait(false);

        // Assert
        var searches = await _context.JobExecutions.AsNoTracking()
            .Where(value => value.JobType == "ReleaseSearchItem")
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(searches, Has.Count.EqualTo(1));
            Assert.That(searches[0].Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(searches[0].Parameters["contentHierarchyItemId"], Is.EqualTo(item.ToString("D")));
        }
    }

    [Test]
    public async Task DispatchAsync_QueuesOneSearchBehindTheRunningOne_WhenMetadataRefreshesDuringASearch()
    {
        // Arrange
        var item = Guid.NewGuid();
        var runningId = await _manager.ExecuteAsync(new JobEnqueueRequest
        {
            JobType = "ReleaseSearchItem",
            Parameters = new Dictionary<string, string> { ["contentHierarchyItemId"] = item.ToString("D") }
        }).ConfigureAwait(false);
        await _context.JobExecutions.Where(value => value.Id == runningId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(value => value.Status, JobExecutionStatus.Running)
                .SetProperty(value => value.NodeId, "node-a")
                .SetProperty(value => value.StartedAt, DateTime.UtcNow)
                .SetProperty(value => value.LastHeartbeat, DateTime.UtcNow))
            .ConfigureAwait(false);

        // Act
        for (var refresh = 0; refresh < 2; refresh++)
        {
            await ((Task)typeof(EventDispatchBackgroundService)
                .GetMethod("DispatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_dispatcher,
                [
                    new MagpieEvent(Guid.NewGuid(), "metadata.refreshed",
                        new Dictionary<string, string>
                        {
                            [MagpieEvents.Keys.ContentHierarchyItemId] = item.ToString("D")
                        },
                        DateTime.UtcNow, DateTime.UtcNow, null),
                    _triggers, _schedules, _rules, _manager, _users, Mock.Of<IDownloadScopeRepository>(),
                    Mock.Of<IContentRepository>(), _hierarchy.Object, _executions, _scopes,
                    Array.Empty<INotificationProvider>(), CancellationToken.None
                ])!).ConfigureAwait(false);
        }

        // Assert
        var searches = await _context.JobExecutions.AsNoTracking()
            .Where(value => value.JobType == "ReleaseSearchItem")
            .OrderBy(value => value.CreatedAt)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(searches.Select(value => value.Status),
                Is.EqualTo([JobExecutionStatus.Running, JobExecutionStatus.Queued]));
            Assert.That(searches[0].Id, Is.EqualTo(runningId));
            Assert.That(searches[1].LockPartition, Is.EqualTo(searches[0].LockPartition).And.Not.Null);
            Assert.That(searches[1].NotBeforeAt, Is.Null);
            Assert.That(searches.Select(value => value.Parameters["contentHierarchyItemId"]),
                Is.All.EqualTo(item.ToString("D")));
        }
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task CanEscalateAsync_TreatsAQueuedSearchAsPending_WhenAMetadataRefreshQueuedIt(
        bool refreshed, bool expected)
    {
        // Arrange
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "season",
            Name = "season",
            Slug = "season",
            EffectiveMonitoring = MonitoringMode.Monitored,
            ContentItem = new ContentItem()
        };
        var first = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = root.Id,
            ContentType = "tv",
            Level = "episode",
            Name = "one",
            Slug = "one",
            EffectiveMonitoring = MonitoringMode.Monitored,
            ContentItem = new ContentItem
            {
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = DateTime.UtcNow.AddDays(-1)
            }
        };
        var second = first with { Id = Guid.NewGuid(), Name = "two", Slug = "two" };
        _context.ScheduleTriggers.Add(new ScheduleTrigger
        {
            JobType = "ReleaseSearchScan",
            Scope = JobScope.Content,
            Name = "search",
            Schedule = "*/30 * * * *",
            Enabled = true,
            Parameters = new()
        });
        _context.JobExecutions.AddRange(Enumerable.Range(0, 50).Select(_ => new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            CreatedAt = DateTime.UtcNow.AddHours(-3)
        }));
        await _context.SaveChangesAsync().ConfigureAwait(false);
        if (refreshed)
        {
            await ((Task)typeof(EventDispatchBackgroundService)
                .GetMethod("DispatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_dispatcher,
                [
                    new MagpieEvent(Guid.NewGuid(), "metadata.refreshed",
                        new Dictionary<string, string>
                        {
                            [MagpieEvents.Keys.ContentHierarchyItemId] = first.Id.ToString("D")
                        },
                        DateTime.UtcNow, DateTime.UtcNow, null),
                    _triggers, _schedules, _rules, _manager, _users, Mock.Of<IDownloadScopeRepository>(),
                    Mock.Of<IContentRepository>(), _hierarchy.Object, _executions, _scopes,
                    Array.Empty<INotificationProvider>(), CancellationToken.None
                ])!).ConfigureAwait(false);
        }

        var searches = new ReleaseSearchService(_hierarchy.Object, Mock.Of<IContentRepository>(),
            Mock.Of<IContentService>(), Mock.Of<IIndexerRepository>(), Mock.Of<IIndexerConnectionRepository>(), [], [],
            Mock.Of<IReleaseRepository>(), Mock.Of<IReleaseScopeRepository>(), Mock.Of<IDownloadRepository>(),
            new ReleaseBlockList(Mock.Of<IDownloadRepository>()),
            Mock.Of<IDownloadClientRepository>(), Mock.Of<IContentTypeRegistry>(), Mock.Of<IRootFolderService>(),
            Mock.Of<IAuditRecordRepository>(), _executions, _manager, _transactions, Mock.Of<IEventBus>(),
            Mock.Of<IReleaseSearchQueryExecutor>(), Mock.Of<IReleaseEvaluationService>(), [],
            new SearchOptions(), TimeProvider.System, NullLogger<ReleaseSearchService>.Instance);

        // Act
        var mayEscalate = await ((Task<bool>)typeof(ReleaseSearchService)
            .GetMethod("CanEscalateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(searches, [root, new[] { root, first, second }, new HashSet<Guid>(), false,
                CancellationToken.None])!).ConfigureAwait(false);

        // Assert
        Assert.That(mayEscalate, Is.EqualTo(expected));
    }
}
