using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Config;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Fulfillment;
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
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ReleaseSearchScanJobPublishDateTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private FakeTimeProvider _time = null!;
    private IJob _subject = null!;
    private IReleaseSearchService _search = null!;

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
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        _jobManager = new Mock<IJobManager>();
        _executions = new Mock<IJobExecutionRepository>();
        _executions
            .Setup(repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        _executions
            .Setup(repository => repository.QueryAllAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobExecution>());
        var hierarchy = new ContentHierarchyItemRepository(
            _context,
            Mock.Of<IAuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IEventBus>(),
            NullLogger<ContentHierarchyItemRepository>.Instance,
            _time,
            Mock.Of<IContentRepository>());
        _subject = new ReleaseSearchScanJob(
            _jobManager.Object,
            hierarchy,
            _time,
            NullLogger<ReleaseSearchScanJob>.Instance,
            Mock.Of<IProgressReporter>());
        _search = new ReleaseSearchService(
            hierarchy,
            Mock.Of<IContentRepository>(),
            Mock.Of<IContentService>(),
            Mock.Of<IIndexerRepository>(),
            Mock.Of<IIndexerConnectionRepository>(),
            [],
            [],
            Mock.Of<IReleaseRepository>(),
            Mock.Of<IReleaseScopeRepository>(),
            Mock.Of<IDownloadRepository>(),
            new ReleaseBlockList(Mock.Of<IDownloadRepository>()),
            Mock.Of<IDownloadClientRepository>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IRootFolderService>(),
            Mock.Of<IAuditRecordRepository>(),
            _executions.Object,
            _jobManager.Object,
            Mock.Of<ITransactionManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseSearchQueryExecutor>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            new SearchOptions(),
            _time,
            NullLogger<ReleaseSearchService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [TestCase(72, null, 0)]
    [TestCase(12, null, 1)]
    [TestCase(-12, null, 1)]
    [TestCase(12, "0", 0)]
    [TestCase(72, "3", 1)]
    public async Task ExecuteAsync_SearchesDatedContent_WhenItsPublishDateIsWithinTheLeadTime(
        int hoursUntilPublished,
        string? leadTimeDays,
        int expectedSearches)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainer = new MetadataContainer { CreatedAt = _time.GetUtcNow().UtcDateTime },
            ContentType = "type-a",
            Level = "movie",
            Slug = "film",
            Name = "Film",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };
        item.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Film",
            PublishDate = _time.GetUtcNow().UtcDateTime.AddHours(hoursUntilPublished),
            CreatedAt = _time.GetUtcNow().UtcDateTime
        });
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var parameters = leadTimeDays is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["leadTimeDays"] = leadTimeDays };

        // Act
        await _subject.ExecuteAsync(new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: Guid.NewGuid(),
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: parameters,
            Event: null)).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "ReleaseSearchItem" &&
                    request.Parameters!["contentHierarchyItemId"] == item.Id.ToString("D")),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedSearches));
    }

    [Test]
    public void DefaultSchedule_SearchesOneDayBeforeThePublishDate_WhenSeeded()
    {
        // Act
        var schedule = DefaultJobTriggerCatalog.ScheduleTriggers.Single(trigger =>
            trigger.JobType == "ReleaseSearchScan");

        // Assert
        Assert.That(schedule.Parameters, Is.EqualTo(new Dictionary<string, string> { ["leadTimeDays"] = "1" }));
    }

    [Test]
    public async Task ExecuteAsync_SearchesContent_WhenItHasNoPublishDate()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainer = new MetadataContainer { CreatedAt = _time.GetUtcNow().UtcDateTime },
            ContentType = "type-a",
            Level = "movie",
            Slug = "film",
            Name = "Film",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };
        item.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Film",
            CreatedAt = _time.GetUtcNow().UtcDateTime
        });
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: Guid.NewGuid(),
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null)).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "ReleaseSearchItem" &&
                    request.Parameters!["contentHierarchyItemId"] == item.Id.ToString("D")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SearchesOnlyTheAiredEpisode_WhenAShowAlsoHasEpisodesNotPublishedYet()
    {
        // Arrange
        var now = _time.GetUtcNow().UtcDateTime;
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "show",
            Slug = "show",
            Name = "Show",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        var aired = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        aired.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Episode 1",
            PublishDate = now.AddDays(-3),
            CreatedAt = now
        });
        var future = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        future.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Episode 2",
            PublishDate = now.AddDays(7),
            CreatedAt = now
        });
        var nextSeason = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        var nextSeasonEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = nextSeason.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "episode",
            Slug = "episode-3",
            Name = "Episode 3",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        nextSeasonEpisode.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Episode 3",
            PublishDate = now.AddDays(30),
            CreatedAt = now
        });
        _context.ContentHierarchyItems.AddRange(show, season, aired, future, nextSeason, nextSeasonEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var enqueued = new List<JobExecution>();
        _jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobEnqueueRequest request, CancellationToken _) =>
            {
                var execution = new JobExecution
                {
                    Id = Guid.NewGuid(),
                    JobType = request.JobType,
                    Parameters = new(request.Parameters!)
                };
                enqueued.Add(execution);
                return execution.Id;
            });
        _executions
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => enqueued.Find(execution => execution.Id == id));

        // Act
        await _subject.ExecuteAsync(new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: Guid.NewGuid(),
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["leadTimeDays"] = "1" },
            Event: null)).ConfigureAwait(false);
        var showSearch = enqueued.Single(execution =>
            execution.Parameters["contentHierarchyItemId"] == show.Id.ToString("D"));
        await _search.SearchAsync(show.Id, null, null, showSearch.Id, null).ConfigureAwait(false);
        var seasonSearch = enqueued.Single(execution =>
            execution.Parameters["contentHierarchyItemId"] == season.Id.ToString("D"));
        await _search.SearchAsync(season.Id, null, null, seasonSearch.Id, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            enqueued.Select(execution => execution.Parameters["contentHierarchyItemId"]),
            Is.EqualTo([show.Id.ToString("D"), season.Id.ToString("D"), aired.Id.ToString("D")]));
    }

    [TestCase(null)]
    [TestCase("1")]
    public async Task SearchAsync_SearchesEveryMissingEpisode_WhenSearchNowTargetsAShowWithAnEpisodeNotPublishedYet(
        string? leadTimeDays)
    {
        // Arrange
        var now = _time.GetUtcNow().UtcDateTime;
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "show",
            Slug = "show",
            Name = "Show",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        var aired = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        aired.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Episode 1",
            PublishDate = now.AddDays(-3),
            CreatedAt = now
        });
        var future = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = now },
            ContentType = "type-a",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = now
        };
        future.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Episode 2",
            PublishDate = now.AddDays(7),
            CreatedAt = now
        });
        _context.ContentHierarchyItems.AddRange(show, season, aired, future);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var searchNow = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = show.Id.ToString("D"),
                ["force"] = "true"
            }
        };
        if (leadTimeDays is not null)
        {
            searchNow.Parameters["leadTimeDays"] = leadTimeDays;
        }

        var enqueued = new List<JobExecution> { searchNow };
        _jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobEnqueueRequest request, CancellationToken _) =>
            {
                var execution = new JobExecution
                {
                    Id = Guid.NewGuid(),
                    JobType = request.JobType,
                    Parameters = new(request.Parameters!)
                };
                enqueued.Add(execution);
                return execution.Id;
            });
        _executions
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => enqueued.Find(execution => execution.Id == id));

        // Act
        await _search.SearchAsync(show.Id, null, null, searchNow.Id, null, force: true).ConfigureAwait(false);
        var seasonSearch = enqueued.Single(execution =>
            execution.Parameters["contentHierarchyItemId"] == season.Id.ToString("D"));
        await _search.SearchAsync(season.Id, null, null, seasonSearch.Id, null, force: true).ConfigureAwait(false);

        // Assert
        Assert.That(
            enqueued
                .Where(execution => execution.Parameters.GetValueOrDefault("searchKind") == "leaf")
                .Select(execution => execution.Parameters["contentHierarchyItemId"]),
            Is.EquivalentTo([aired.Id.ToString("D"), future.Id.ToString("D")]));
    }
}
