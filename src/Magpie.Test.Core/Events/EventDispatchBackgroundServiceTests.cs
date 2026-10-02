using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Services;
using Magpie.Test.Core.Jobs.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

using EventCatalog = Magpie.Sdk.Events.MagpieEvents;

namespace Magpie.Test.Core.Events;

[TestFixture]
public sealed class EventDispatchBackgroundServiceTests
{
    private static readonly Guid ItemId = new("00000000-0000-0000-0000-000000000001");

    private static readonly Guid SearchId = new("00000000-0000-0000-0000-00000000000a");

    private static readonly Guid EventTriggerId = new("00000000-0000-0000-0000-00000000000b");

    private static readonly Guid ParentExecutionId = new("00000000-0000-0000-0000-00000000000c");

    private static readonly Guid CorrelationId = new("00000000-0000-0000-0000-00000000000d");

    private Mock<IEventTriggerRepository> _eventTriggers = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private Mock<IJobManager> _jobManager = null!;
    private EventTrigger _marker = null!;
    private JobExecution _queued = null!;
    private TaskCompletionSource _marked = null!;
    private ServiceProvider _services = null!;
    private EventBus _eventBus = null!;
    private EventDispatchBackgroundService _subject = null!;

    public static IEnumerable<TestCaseData> NoLongerMatchingCases
    {
        get
        {
            yield return new TestCaseData((JobExecution?)null);
            yield return new TestCaseData(new JobExecution
            {
                Id = SearchId,
                JobType = MagpieJobs.Types.ReleaseSearchItem,
                ExecutionMode = JobExecutionMode.Queue,
                EventTriggerId = EventTriggerId,
                Status = JobExecutionStatus.Completed,
                Parameters = new() { [MagpieJobs.Parameters.ContentHierarchyItemId] = ItemId.ToString("D") }
            });
            yield return new TestCaseData(new JobExecution
            {
                Id = SearchId,
                JobType = MagpieJobs.Types.ReleaseSearchItem,
                ExecutionMode = JobExecutionMode.Queue,
                EventTriggerId = EventTriggerId,
                Status = JobExecutionStatus.Queued,
                Parameters = new()
                {
                    [MagpieJobs.Parameters.ContentHierarchyItemId] = ItemId.ToString("D"),
                    [MagpieJobs.Parameters.Force] = "true"
                }
            });
        }
    }

    [SetUp]
    public void SetUp()
    {
        _marked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _marker = new EventTrigger
        {
            JobType = "Marker",
            Scope = JobScope.User,
            EventFilter = new EventFilter(),
            Name = "Marker"
        };
        _queued = new JobExecution
        {
            Id = SearchId,
            JobType = MagpieJobs.Types.ReleaseSearchItem,
            ExecutionMode = JobExecutionMode.Queue,
            EventTriggerId = EventTriggerId,
            Status = JobExecutionStatus.Queued,
            Parameters = new() { [MagpieJobs.Parameters.ContentHierarchyItemId] = ItemId.ToString("D") },
            ModifiedAt = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc)
        };
        _eventTriggers = new Mock<IEventTriggerRepository>();
        _eventTriggers
            .Setup(repository => repository.QueryAllAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([_marker]);
        var rules = new Mock<INotificationRuleRepository>();
        rules
            .Setup(repository => repository.QueryAllAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var users = new Mock<IUserRepository>();
        users
            .Setup(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy
            .Setup(repository => repository.GetByIdAsync(ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = ItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "item",
                Name = "Item",
                ContentItem = new ContentItem { Fulfillment = FulfillmentStatus.Fulfilled }
            });
        hierarchy
            .Setup(repository => repository.QueryIdsAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([ItemId]);
        _executions = new Mock<IJobExecutionRepository>();
        _executions
            .Setup(repository => repository.QueryAllAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([_queued]);
        _executions
            .Setup(repository => repository.CancelAsync(_queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCancellation?)null);
        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(manager => manager.GetSpecs(It.IsAny<JobTriggerMode?>()))
            .Returns([]);
        _jobManager
            .Setup(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == "Marker"),
                It.IsAny<CancellationToken>()))
            .Callback(() => _marked.TrySetResult());
        var scopes = new Mock<IJobExecutionScopeFactory>();
        scopes
            .Setup(factory => factory.CreateScope(ParentExecutionId))
            .Returns(Mock.Of<IJobExecutionScope>());
        _services = new ServiceCollection()
            .AddSingleton(_eventTriggers.Object)
            .AddSingleton(Mock.Of<IScheduleTriggerRepository>())
            .AddSingleton(rules.Object)
            .AddSingleton(_jobManager.Object)
            .AddSingleton(scopes.Object)
            .AddSingleton(users.Object)
            .AddSingleton(Mock.Of<IDownloadScopeRepository>())
            .AddSingleton(Mock.Of<IContentRepository>())
            .AddSingleton(hierarchy.Object)
            .AddSingleton(_executions.Object)
            .BuildServiceProvider();
        var migration = new DatabaseMigrationState();
        migration.Complete();
        var hostEnvironment = new MagpieHostEnvironment(
            "dispatch-test",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc));
        var time = new FakeTimeProvider();
        _eventBus = new EventBus(hostEnvironment, scopes.Object, time);
        _subject = new EventDispatchBackgroundService(
            _eventBus,
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            time,
            migration);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subject.StopAsync(CancellationToken.None).ConfigureAwait(false);
        _subject.Dispose();
        _eventBus.Dispose();
        await _services.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public void EventTriggers_PartitionTheAutoGrabPerContentItem_WhenCatalogInspected()
    {
        // Act
        var trigger = DefaultJobTriggerCatalog.EventTriggers
            .Single(candidate => candidate.JobType == "ReleaseAutoGrabItem");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReleaseAutoGrabItemJob.Spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerTypeAndTarget));
            Assert.That(ReleaseAutoGrabItemJob.Spec.LockPartitionFields, Is.EqualTo(["contentItemId"]));
            Assert.That(trigger.Parameters, Does.ContainKey("contentItemId"));
        }
    }

    [Test]
    public void EventTriggers_BindNoJob_WhenTheEventIsReleaseScopeMatched()
    {
        // Act
        var triggers = DefaultJobTriggerCatalog.EventTriggers
            .Where(candidate => candidate.EventFilter.EventName?.Value == "release.scopeMatched");

        // Assert
        Assert.That(
            triggers,
            Is.Empty,
            "The additional scopes of a pack rely on the auto-grab scan schedule.");
    }

    [TestCase("${event.content.hierarchyItemId}", 0)]
    [TestCase("00000000-0000-0000-0000-000000000001", 0)]
    [TestCase("00000000-0000-0000-0000-000000000002", 1)]
    [TestCase("$${event.content.hierarchyItemId}", 1)]
    public async Task ExecuteAsync_SuppressesMetadataRefreshTrigger_WhenTheTargetIsSet(
        string target,
        int expectedEnqueues)
    {
        // Arrange
        var refresh = new EventTrigger
        {
            JobType = MagpieJobs.Types.ContentMetadataRefreshItem,
            Scope = JobScope.User,
            EventFilter = new EventFilter(),
            Name = "Refresh",
            Parameters = new() { [MagpieJobs.Parameters.ContentHierarchyItemId] = target }
        };
        _eventTriggers
            .Setup(repository => repository.QueryAllAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([refresh, _marker]);
        _jobManager
            .Setup(manager => manager.GetExecutionAsync(ParentExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecution
            {
                Id = ParentExecutionId,
                JobType = MagpieJobs.Types.ContentMetadataRefreshItem,
                CorrelationId = CorrelationId
            });
        _jobManager
            .Setup(manager => manager.QueryExecutionsAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = MagpieJobs.Types.ContentMetadataRefreshItem,
                        ExecutionMode = JobExecutionMode.Queue,
                        ParentExecutionId = ParentExecutionId,
                        CorrelationId = CorrelationId,
                        Parameters = new() { [MagpieJobs.Parameters.ContentHierarchyItemId] = ItemId.ToString("D") }
                    }
                ]
            });
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        await _eventBus.PublishAsync(new MagpieEvent(
            new Guid("00000000-0000-0000-0000-000000000010"),
            EventCatalog.Names.ContentHierarchyItemCreated,
            new Dictionary<string, string> { [EventCatalog.Keys.ContentHierarchyItemId] = ItemId.ToString("D") },
            new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            null)
        {
            JobExecutionId = ParentExecutionId
        }).ConfigureAwait(false);
        await _marked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == MagpieJobs.Types.ContentMetadataRefreshItem),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedEnqueues));
    }

    [Test]
    public async Task ExecuteAsync_CancelsTheSearch_WhenItChangesTwiceBeforeTheCancel()
    {
        // Arrange
        var running = _queued with
        {
            Status = JobExecutionStatus.Running,
            ModifiedAt = new DateTime(2026, 9, 28, 10, 1, 0, DateTimeKind.Utc)
        };
        var requeued = _queued with
        {
            Status = JobExecutionStatus.Queued,
            ModifiedAt = new DateTime(2026, 9, 28, 10, 2, 0, DateTimeKind.Utc)
        };
        _executions
            .SetupSequence(repository => repository.GetByIdAsync(SearchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(running)
            .ReturnsAsync(requeued);
        _jobManager
            .Setup(manager => manager.CancelAsync(SearchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _executions
            .Setup(repository => repository.CancelAsync(requeued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCancellation(null, true));
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        await _eventBus.PublishAsync(new MagpieEvent(
            new Guid("00000000-0000-0000-0000-000000000010"),
            EventCatalog.Names.ContentFulfillmentChanged,
            new Dictionary<string, string> { [EventCatalog.Keys.ContentHierarchyItemId] = ItemId.ToString("D") },
            new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            null)).ConfigureAwait(false);
        await _marked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        _executions.Verify(
            repository => repository.CancelAsync(requeued, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCaseSource(nameof(NoLongerMatchingCases))]
    public async Task ExecuteAsync_StopsCancellingTheSearch_WhenTheSearchNoLongerMatches(
        JobExecution? reread)
    {
        // Arrange
        _executions
            .Setup(repository => repository.GetByIdAsync(SearchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reread);
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        await _eventBus.PublishAsync(new MagpieEvent(
            new Guid("00000000-0000-0000-0000-000000000010"),
            EventCatalog.Names.ContentFulfillmentChanged,
            new Dictionary<string, string> { [EventCatalog.Keys.ContentHierarchyItemId] = ItemId.ToString("D") },
            new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            null)).ConfigureAwait(false);
        await _marked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _executions.Verify(
                repository => repository.GetByIdAsync(SearchId, It.IsAny<CancellationToken>()),
                Times.Once);
            _executions.Verify(
                repository => repository.CancelAsync(It.IsAny<JobExecution>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                manager => manager.CancelAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
