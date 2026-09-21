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
    private DatabaseMigrationState _migrationState = null!;

    [SetUp]
    public void SetUp()
    {
        _migrationState = new DatabaseMigrationState();
        _migrationState.Complete();
    }

    [Test]
    public async Task ExecuteAsync_RefreshesCachedCatalogs_WhenPeerPublishesAnyCatalogChange()
    {
        // Arrange
        var triggers = new Mock<IEventTriggerRepository> { CallBase = true };
        triggers.Setup(value => value.QueryAsync(It.IsAny<EventTriggerQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger>());
        var rules = new Mock<INotificationRuleRepository> { CallBase = true };
        rules.Setup(value => value.QueryAsync(It.IsAny<NotificationRuleQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var users = new Mock<IUserRepository> { CallBase = true };
        users.Setup(value => value.QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new() { Id = Guid.NewGuid(), Username = "system", ProviderType = "local" }],
            });
        var jobs = new Mock<IJobManager>();
        jobs.Setup(value => value.GetSpecs(null)).Returns([]);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloadScopes = new Mock<IDownloadScopeRepository> { CallBase = true };
        downloadScopes.Setup(value => value.QueryAsync(It.IsAny<DownloadScopeQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .Callback(() => completed.TrySetResult()).ReturnsAsync(new PagedList<DownloadScope>());
        var services = new ServiceCollection();
        services.AddSingleton(triggers.Object);
        services.AddSingleton(rules.Object);
        services.AddSingleton(users.Object);
        services.AddSingleton(jobs.Object);
        services.AddSingleton(downloadScopes.Object);
        services.AddSingleton(Mock.Of<IScheduleTriggerRepository>());
        services.AddSingleton(Mock.Of<IContentRepository>());
        services.AddSingleton(Mock.Of<IContentHierarchyItemRepository>());
        services.AddSingleton(Mock.Of<IJobExecutionRepository>());
        services.AddSingleton(Mock.Of<IJobExecutionScopeFactory>());
        using var provider = services.BuildServiceProvider();
        var environment = new MagpieHostEnvironment("local", true, false, "", "", "", "", DateTime.UtcNow);
        using var bus = new EventBus(environment, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventDispatchBackgroundService>.Instance,
            environment,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            _migrationState);
        var first = new MagpieEvent(Guid.NewGuid(), "first", new Dictionary<string, string>(),
            DateTime.UtcNow, DateTime.UtcNow, null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await bus.PublishAsync(first).ConfigureAwait(false);
        await bus.PublishAsync(first with { Name = "second" }).ConfigureAwait(false);
        await bus.PublishAsync(first with { Name = "eventTrigger.replaced", NodeId = "peer" })
            .ConfigureAwait(false);
        await bus.PublishAsync(first with { Name = "notificationRule.replaced", NodeId = "peer" })
            .ConfigureAwait(false);
        await bus.PublishAsync(first with { Name = "user.replaced", NodeId = "peer" })
            .ConfigureAwait(false);
        await bus.PublishAsync(first with
        {
            Name = "download.failed",
            Context = new Dictionary<string, string>
            {
                ["download.id"] = Guid.NewGuid().ToString("D"),
            },
        }).ConfigureAwait(false);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            triggers.Verify(value => value.QueryAsync(It.IsAny<EventTriggerQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            rules.Verify(value => value.QueryAsync(It.IsAny<NotificationRuleQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            users.Verify(value => value.QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_QueriesTheSystemUserOnce_WhenNoSystemUserExists()
    {
        // Arrange
        var triggers = new Mock<IEventTriggerRepository> { CallBase = true };
        triggers.Setup(value => value.QueryAsync(It.IsAny<EventTriggerQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger>());
        var rules = new Mock<INotificationRuleRepository> { CallBase = true };
        rules.Setup(value => value.QueryAsync(It.IsAny<NotificationRuleQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var users = new Mock<IUserRepository> { CallBase = true };
        users.Setup(value => value.QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var jobs = new Mock<IJobManager>();
        jobs.Setup(value => value.GetSpecs(null)).Returns([]);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloadScopes = new Mock<IDownloadScopeRepository> { CallBase = true };
        downloadScopes.Setup(value => value.QueryAsync(It.IsAny<DownloadScopeQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .Callback(() => completed.TrySetResult()).ReturnsAsync(new PagedList<DownloadScope>());
        var services = new ServiceCollection();
        services.AddSingleton(triggers.Object);
        services.AddSingleton(rules.Object);
        services.AddSingleton(users.Object);
        services.AddSingleton(jobs.Object);
        services.AddSingleton(downloadScopes.Object);
        services.AddSingleton(Mock.Of<IScheduleTriggerRepository>());
        services.AddSingleton(Mock.Of<IContentRepository>());
        services.AddSingleton(Mock.Of<IContentHierarchyItemRepository>());
        services.AddSingleton(Mock.Of<IJobExecutionRepository>());
        services.AddSingleton(Mock.Of<IJobExecutionScopeFactory>());
        using var provider = services.BuildServiceProvider();
        var environment = new MagpieHostEnvironment("local", true, false, "", "", "", "", DateTime.UtcNow);
        using var bus = new EventBus(environment, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventDispatchBackgroundService>.Instance,
            environment,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            _migrationState);
        var first = new MagpieEvent(Guid.NewGuid(), "first", new Dictionary<string, string>(),
            DateTime.UtcNow, DateTime.UtcNow, null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await bus.PublishAsync(first).ConfigureAwait(false);
        await bus.PublishAsync(first with { Name = "second" }).ConfigureAwait(false);
        await bus.PublishAsync(first with { Name = "third" }).ConfigureAwait(false);
        await bus.PublishAsync(first with
        {
            Name = "download.failed",
            Context = new Dictionary<string, string>
            {
                ["download.id"] = Guid.NewGuid().ToString("D"),
            },
        }).ConfigureAwait(false);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        users.Verify(value => value.QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter>(),
            It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(FulfillmentStatus.Fulfilled, true, false, 0)]
    [TestCase(FulfillmentStatus.FulfilledExternally, true, false, 0)]
    [TestCase(FulfillmentStatus.Missing, true, false, 1)]
    [TestCase(FulfillmentStatus.Upgradable, true, false, 1)]
    [TestCase(FulfillmentStatus.Partial, true, false, 1)]
    [TestCase(FulfillmentStatus.Fulfilled, false, true, 1)]
    [TestCase(FulfillmentStatus.Fulfilled, false, false, 0)]
    [TestCase(null, true, false, 1)]
    public async Task ExecuteAsync_EnqueuesTheAutomaticReleaseSearch_WhenTheRefreshedItemNeedsOne(
        FulfillmentStatus? fulfillment,
        bool leaf,
        bool hasAcquisitionCandidate,
        int expectedSearches)
    {
        // Arrange
        var contentHierarchyItemId = Guid.Parse("b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6");
        var decided = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([ReleaseSearchItemJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == "ReleaseSearchItem")
                {
                    decided.TrySetResult();
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var executionRepository = new Mock<IJobExecutionRepository> { CallBase = true };
        executionRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((execution, _, _) =>
            {
                if (execution.JobType == "ReleaseSearchItem")
                {
                    decided.TrySetResult();
                }
            })
            .ReturnsAsync((JobExecution execution, Guid? _, CancellationToken _) => execution);
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        contentHierarchyItemRepository
            .Setup(repository => repository.GetByIdAsync(contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = contentHierarchyItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "refreshed",
                Name = "Refreshed",
                ContentItem = fulfillment is { } status ? new ContentItem { Fulfillment = status } : null,
            });
        contentHierarchyItemRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.Ids != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaf ? [contentHierarchyItemId] : []);
        contentHierarchyItemRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria =>
                    criteria.AncestorId == contentHierarchyItemId &&
                    criteria.LeafOnly == true &&
                    criteria.EffectiveMonitoring == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasAcquisitionCandidate ? [Guid.NewGuid()] : []);
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [.. DefaultJobTriggerCatalog.EventTriggers] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider.Setup(p => p.GetService(typeof(IJobManager))).Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider.Setup(p => p.GetService(typeof(IUserRepository))).Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(contentHierarchyItemRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(executionRepository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "metadata.refreshed",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = "b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        await decided.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "ReleaseSearchItem" &&
                    request.Parameters!["contentHierarchyItemId"] ==
                        "b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedSearches));
    }

    [Test]
    public async Task ExecuteAsync_CancelsOnlyTheAutomaticReleaseSearches_WhenTheItemNeedsNoRelease()
    {
        // Arrange
        var contentHierarchyItemId = Guid.Parse("b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6");
        var failedId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var automaticId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var manualId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var forcedId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionRepository = new Mock<IJobExecutionRepository> { CallBase = true };
        executionRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.JobType == "ReleaseSearchItem" &&
                    query.Statuses != null &&
                    query.Statuses.Contains(JobExecutionStatus.Queued) &&
                    query.Statuses.Contains(JobExecutionStatus.Running) &&
                    query.ParameterValueReferences != null &&
                    query.ParameterValueReferences.Contains(contentHierarchyItemId.ToString("D"))),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = failedId,
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = contentHierarchyItemId.ToString("D")
                        },
                        ExecutionMode = JobExecutionMode.Queue,
                        Status = JobExecutionStatus.Running,
                        ParentExecutionId = Guid.NewGuid(),
                    },
                    new JobExecution
                    {
                        Id = automaticId,
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = contentHierarchyItemId.ToString("D")
                        },
                        ExecutionMode = JobExecutionMode.Queue,
                        Status = JobExecutionStatus.Queued,
                        EventTriggerId = Guid.NewGuid(),
                    },
                    new JobExecution
                    {
                        Id = manualId,
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = contentHierarchyItemId.ToString("D")
                        },
                        ExecutionMode = JobExecutionMode.Queue,
                        Status = JobExecutionStatus.Queued,
                    },
                    new JobExecution
                    {
                        Id = forcedId,
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = contentHierarchyItemId.ToString("D"),
                            ["force"] = "true",
                        },
                        ExecutionMode = JobExecutionMode.Queue,
                        Status = JobExecutionStatus.Queued,
                        EventTriggerId = Guid.NewGuid(),
                    },
                ]
            });
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([ReleaseSearchItemJob.Spec]);
        jobManager
            .Setup(manager => manager.RequestCancellationAsync(failedId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cancellation failed."));
        jobManager
            .Setup(manager => manager.RequestCancellationAsync(automaticId, It.IsAny<CancellationToken>()))
            .Callback(() => cancelled.TrySetResult())
            .ReturnsAsync(true);
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        contentHierarchyItemRepository
            .Setup(repository => repository.GetByIdAsync(contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = contentHierarchyItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "fulfilled",
                Name = "Fulfilled",
                ContentItem = new ContentItem { Fulfillment = FulfillmentStatus.Fulfilled },
            });
        contentHierarchyItemRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.Ids != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([contentHierarchyItemId]);
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger>());
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider.Setup(p => p.GetService(typeof(IJobManager))).Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider.Setup(p => p.GetService(typeof(IUserRepository))).Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(contentHierarchyItemRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(executionRepository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.fulfillment.changed",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = "b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            jobManager.Verify(
                manager => manager.RequestCancellationAsync(failedId, It.IsAny<CancellationToken>()),
                Times.Once);
            jobManager.Verify(
                manager => manager.RequestCancellationAsync(automaticId, It.IsAny<CancellationToken>()),
                Times.Once);
            jobManager.Verify(
                manager => manager.RequestCancellationAsync(manualId, It.IsAny<CancellationToken>()),
                Times.Never);
            jobManager.Verify(
                manager => manager.RequestCancellationAsync(forcedId, It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase("${event.contentId}", "42")]
    [TestCase("{{left}} and {{right}}", "{{left}} and {{right}}")]
    [TestCase("id=${event.id} $${raw}", "id=42 ${raw}")]
    [TestCase("static value", "static value")]
    [TestCase("${event.extension.customKey}", "custom value")]
    [TestCase("{\"filter\":{\"id\":1}}", "{\"filter\":{\"id\":1}}")]
    public async Task ExecuteAsync_ResolvesTheTriggerParameter_WhenTheStoredValueBindsToTheEvent(
        string storedValue,
        string expected)
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Bound parameter",
            EventFilter = new EventFilter(),
            Parameters = new Dictionary<string, string> { ["itemId"] = storedValue },
            Enabled = true,
        };
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>
            {
                ["contentId"] = "42",
                ["id"] = "42",
                ["extension.customKey"] = "custom value",
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);
        var dispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
                Parameters = [new JobParameter("itemId", null, DataType.String, true, null)],
            }
        ]);
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => dispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider.Setup(provider => provider.GetService(typeof(IJobManager))).Returns(jobManager.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider.Setup(provider => provider.GetService(typeof(IUserRepository))).Returns(userRepository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(request.Parameters!["itemId"], Is.EqualTo(expected));
    }

    [TestCase("${event.contentId", DataType.String, "Event binding is malformed.")]
    [TestCase("${event.}", DataType.String, "Event binding is malformed.")]
    [TestCase("${event.content{Id}", DataType.String, "Event binding is malformed.")]
    [TestCase("${other.key}", DataType.String, "Event binding is malformed.")]
    [TestCase("${event.content.itemId}", DataType.Number, "Parameter itemId must be a valid number.")]
    public async Task ExecuteAsync_PersistsAFailedExecution_WhenTheTriggerBindingIsInvalid(
        string storedValue,
        DataType parameterType,
        string expectedMessage)
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Invalid binding",
            EventFilter = new EventFilter(),
            Parameters = new Dictionary<string, string> { ["itemId"] = storedValue },
            Enabled = true,
        };
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string> { ["content.itemId"] = "not-a-number" },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);
        var persistedCompletion = new TaskCompletionSource<JobExecution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionRepository = new Mock<IJobExecutionRepository> { CallBase = true };
        executionRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((execution, _, _) =>
                persistedCompletion.TrySetResult(execution))
            .ReturnsAsync((JobExecution execution, Guid? _, CancellationToken _) => execution);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
                Parameters = [new JobParameter("itemId", null, parameterType, true, null)],
            }
        ]);
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider.Setup(provider => provider.GetService(typeof(IJobManager))).Returns(jobManager.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider.Setup(provider => provider.GetService(typeof(IUserRepository))).Returns(userRepository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(executionRepository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var persisted = await persistedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted.Status, Is.EqualTo(JobExecutionStatus.Failed));
            Assert.That(persisted.EventTriggerId, Is.EqualTo(trigger.Id));
            Assert.That(persisted.StatusMessage, Is.EqualTo(expectedMessage));
            jobManager.Verify(
                manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }
    }

    [Test]
    public async Task ExecuteAsync_PersistsOneFailureAndTerminates_WhenBindingKeyIsMissing()
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Broken binding",
            EventFilter = new EventFilter(),
            Parameters = new Dictionary<string, string> { ["itemId"] = "${event.content.itemId}" },
            Enabled = true,
        };
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);
        var persistedCompletion = new TaskCompletionSource<JobExecution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionRepository = new Mock<IJobExecutionRepository> { CallBase = true };
        executionRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((execution, _, _) =>
                persistedCompletion.TrySetResult(execution))
            .ReturnsAsync((JobExecution execution, Guid? _, CancellationToken _) => execution);
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
                Parameters = [new JobParameter("itemId", null, DataType.Number, true, null)],
            }
        ]);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider.Setup(provider => provider.GetService(typeof(IJobManager))).Returns(jobManager.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider.Setup(provider => provider.GetService(typeof(IUserRepository))).Returns(userRepository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IJobExecutionRepository)))
            .Returns(executionRepository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var persisted = await persistedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted.Status, Is.EqualTo(JobExecutionStatus.Failed));
            Assert.That(persisted.EventTriggerId, Is.EqualTo(trigger.Id));
            Assert.That(persisted.StatusMessage, Is.EqualTo("Event context key content.itemId is missing."));
            executionRepository.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<JobExecution>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once());
            jobManager.Verify(
                manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesNotification_WhenJobExecutionFailedEventMatchesRule()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "Job events",
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.Regex,
                    Value = "^job\\.",
                },
            },
            ProviderType = "test",
            Enabled = true,
        };
        var notificationQueued = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([]);
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == "SendNotification")
                {
                    notificationQueued.TrySetResult(request);
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger>());
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule> { Items = [rule] });
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var provider = new Mock<INotificationProvider>();
        provider.SetupGet(candidate => candidate.ProviderType).Returns("test");
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(new[] { provider.Object });
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider.Setup(candidate => candidate.GetService(typeof(IJobManager))).Returns(jobManager.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            EventCatalog.Names.JobExecutionFailed,
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await notificationQueued.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.JobType, Is.EqualTo("SendNotification"));
            Assert.That(request.Parameters!["ruleId"], Is.EqualTo(rule.Id.ToString("D")));
            Assert.That(request.TriggerContext!.ToMagpieEvent()!.Name, Is.EqualTo("job.execution.failed"));
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsNotification_WhenSendNotificationJobFailed()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = new Guid("2c0e7e63-5ccf-44a5-9103-b8e4ce0d7ad0"),
            Name = "Job events",
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.Regex,
                    Value = "^job\\.",
                },
            },
            ProviderType = "test",
            Enabled = true,
        };
        var notificationQueued = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([]);
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == "SendNotification")
                {
                    notificationQueued.TrySetResult(request);
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger>());
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule> { Items = [rule] });
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var provider = new Mock<INotificationProvider>();
        provider.SetupGet(candidate => candidate.ProviderType).Returns("test");
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(new[] { provider.Object });
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider.Setup(candidate => candidate.GetService(typeof(IJobManager))).Returns(jobManager.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(candidate => candidate.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var deliveryFailure = new MagpieEvent(
            Guid.NewGuid(),
            EventCatalog.Names.JobExecutionFailed,
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ExecutionJobType] = "SendNotification",
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);
        var ordinaryFailure = new MagpieEvent(
            new Guid("a236dff8-4f4b-4926-b0e6-56ea98daac3d"),
            EventCatalog.Names.JobExecutionFailed,
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ExecutionJobType] = "ContentScan",
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(deliveryFailure).ConfigureAwait(false);
        await eventBus.PublishAsync(ordinaryFailure).ConfigureAwait(false);
        var request = await notificationQueued.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                request.Parameters![JobExecutionDedup.ParameterKey],
                Is.EqualTo("notification:2c0e7e635ccf44a59103b8e4ce0d7ad0:a236dff84f4b4926b0e656ea98daac3d"));
            jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(candidate =>
                        candidate.JobType == "SendNotification"),
                    It.IsAny<CancellationToken>()),
                Times.Once());
        }
    }

    [TestCase("content.added", "^content\\.", false, true)]
    [TestCase("download.completed", "^content\\.", false, false)]
    [TestCase("content.added", "^content\\.", true, false)]
    [TestCase("download.completed", "^content\\.", true, true)]
    public async Task ExecuteAsync_DispatchesExpectedJob_WhenEventNameRegexIsEvaluated(
        string eventName,
        string pattern,
        bool negate,
        bool expected)
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Regex trigger",
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.Regex,
                    Value = pattern,
                    Negate = negate,
                },
            },
            Enabled = true,
        };
        var sentinelTrigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SentinelJob",
            Scope = JobScope.Content,
            Name = "Dispatch sentinel",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var dispatchCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
                Parameters = [new JobParameter("contentHierarchyItemId", null, DataType.String, true, null)],
            },
            new JobSpec
            {
                JobType = sentinelTrigger.JobType,
                DisplayName = "Sentinel job",
                Category = "Test",
                Scope = sentinelTrigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == sentinelTrigger.JobType)
                {
                    dispatchCompleted.TrySetResult();
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger, sentinelTrigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            eventName,
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        await dispatchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == trigger.JobType),
                It.IsAny<CancellationToken>()),
            expected ? Times.Once() : Times.Never());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_DoesNotDispatch_WhenRegexPatternIsInvalid(bool negate)
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Invalid regex trigger",
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.Regex,
                    Value = "([",
                    Negate = negate,
                },
            },
            Enabled = true,
        };
        var sentinelTrigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SentinelJob",
            Scope = JobScope.Content,
            Name = "Dispatch sentinel",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var dispatchCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            },
            new JobSpec
            {
                JobType = sentinelTrigger.JobType,
                DisplayName = "Sentinel job",
                Category = "Test",
                Scope = sentinelTrigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == sentinelTrigger.JobType)
                {
                    dispatchCompleted.TrySetResult();
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger, sentinelTrigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        await dispatchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == trigger.JobType),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DispatchesJob_WhenRegexMatchesContextValue()
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Context regex trigger",
            EventFilter = new EventFilter
            {
                EventContext = new DictionaryCriteria<PropertyCriterion<string>>
                {
                    Items =
                    {
                        ["title"] = new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.Regex,
                            Value = "Matrix",
                        },
                    },
                },
            },
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>
            {
                ["title"] = "The Matrix"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(request.JobType, Is.EqualTo("SomeJob"));
    }

    [Test]
    public async Task ExecuteAsync_CopiesEventProvenanceOntoEveryRequest_WhenTheEventCarriesIt()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Provenance trigger",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "Provenance rule",
            EventFilter = new EventFilter(),
            ProviderType = "test",
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == "SendNotification")
                {
                    notificationDispatched.TrySetResult(request);
                }
                else
                {
                    requestDispatched.TrySetResult(request);
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule> { Items = [rule] });

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(new[] { Mock.Of<INotificationProvider>(p => p.ProviderType == "test") });
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>
            {
                ["title"] = "The Matrix"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null)
        {
            CorrelationId = correlationId,
            TraceContext = new Dictionary<string, string>
            {
                ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
            }
        };

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var notification = await notificationDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.CorrelationId, Is.EqualTo(correlationId));
            Assert.That(
                request.TriggerContext!["traceparent"],
                Is.EqualTo("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"));
            Assert.That(notification.CorrelationId, Is.EqualTo(correlationId));
            Assert.That(
                notification.TriggerContext!["traceparent"],
                Is.EqualTo("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"));
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesTheAmbientPublisherCorrelation_WhenEventCorrelationIsNull()
    {
        // Arrange
        var parentExecutionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Correlation default trigger",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "Correlation default rule",
            EventFilter = new EventFilter(),
            ProviderType = "test",
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == "SendNotification")
                {
                    notificationDispatched.TrySetResult(request);
                }
                else
                {
                    requestDispatched.TrySetResult(request);
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var parentScope = new Mock<IJobExecutionScope>();
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        jobScopeFactory.Setup(f => f.CreateScope(parentExecutionId)).Returns(parentScope.Object);
        jobScopeFactory.SetupGet(f => f.CurrentExecutionId).Returns(parentExecutionId);
        jobScopeFactory.SetupGet(f => f.CurrentCorrelationId).Returns(correlationId);
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule> { Items = [rule] });

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(new[] { Mock.Of<INotificationProvider>(p => p.ProviderType == "test") });
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>
            {
                ["title"] = "The Matrix"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null) with
        {
            JobExecutionId = parentExecutionId,
        };

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var notification = await notificationDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.CorrelationId, Is.EqualTo(correlationId));
            Assert.That(notification.CorrelationId, Is.EqualTo(correlationId));
            jobManager.Verify(
                m => m.GetExecutionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsPublisherCorrelation_WhenEventSetsOne()
    {
        // Arrange
        var parentExecutionId = Guid.NewGuid();
        var publisherCorrelationId = Guid.NewGuid();
        var executionCorrelationId = Guid.NewGuid();
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Publisher-wins trigger",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.GetExecutionAsync(parentExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecution { JobType = "PublisherJob", CorrelationId = executionCorrelationId });
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var parentScope = new Mock<IJobExecutionScope>();
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        jobScopeFactory.Setup(f => f.CreateScope(parentExecutionId)).Returns(parentScope.Object);
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>
            {
                ["title"] = "The Matrix"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null) with
        {
            JobExecutionId = parentExecutionId,
            CorrelationId = publisherCorrelationId,
        };

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(request.CorrelationId, Is.EqualTo(publisherCorrelationId));
        jobManager.Verify(
            m => m.GetExecutionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_LeavesCorrelationNull_WhenEventHasNoJobExecutionId()
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "No-correlation trigger",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>
            {
                ["title"] = "The Matrix"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(request.CorrelationId, Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_SuppressesMetadataRefreshTrigger_WhenDirectCascadeMatches()
    {
        // Arrange
        var contentHierarchyItemId = Guid.NewGuid();
        var parentExecutionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var parent = new JobExecution
        {
            Id = parentExecutionId,
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.Content,
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = Guid.NewGuid().ToString("D")
            },
            ExecutionMode = JobExecutionMode.Queue,
            Status = JobExecutionStatus.Queued,
            CorrelationId = correlationId,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
        };
        var child = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.Content,
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] =
                    contentHierarchyItemId.ToString("D")
            },
            ExecutionMode = JobExecutionMode.Queue,
            Status = JobExecutionStatus.Queued,
            CorrelationId = correlationId,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            ParentExecutionId = parentExecutionId,
        };
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.Content,
            Name = "Refresh created content hierarchy item",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var sentinelTrigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SentinelJob",
            Scope = JobScope.Content,
            Name = "Dispatch sentinel",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var dispatchCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            ContentMetadataRefreshItemJob.Spec,
            new JobSpec
            {
                JobType = sentinelTrigger.JobType,
                DisplayName = "Sentinel job",
                Category = "Test",
                Scope = sentinelTrigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.GetExecutionAsync(parentExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);
        jobManager
            .Setup(m => m.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.JobType == "ContentMetadataRefreshItem" &&
                    query.CorrelationId == correlationId &&
                    query.ParameterValueReferences != null &&
                    query.ParameterValueReferences.Contains(contentHierarchyItemId.ToString("D"))),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 500),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [child] });
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                if (request.JobType == sentinelTrigger.JobType)
                {
                    dispatchCompleted.TrySetResult();
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger, sentinelTrigger] });
        var parentScope = new Mock<IJobExecutionScope>();
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        jobScopeFactory.Setup(f => f.CreateScope(parentExecutionId)).Returns(parentScope.Object);
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            EventCatalog.Names.ContentHierarchyItemCreated,
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = contentHierarchyItemId.ToString("D"),
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null) with
        {
            JobExecutionId = parentExecutionId,
        };

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        await dispatchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.EventTriggerId == trigger.Id),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DispatchesMetadataRefreshTrigger_WhenDirectCascadeIsMissing()
    {
        // Arrange
        var contentHierarchyItemId = Guid.NewGuid();
        var parentExecutionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var parent = new JobExecution
        {
            Id = parentExecutionId,
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.Content,
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = Guid.NewGuid().ToString("D")
            },
            ExecutionMode = JobExecutionMode.Queue,
            Status = JobExecutionStatus.Queued,
            CorrelationId = correlationId,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
        };
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.Content,
            Name = "Refresh created content hierarchy item",
            EventFilter = new EventFilter(),
            Parameters = new Dictionary<string, string>(),
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([ContentMetadataRefreshItemJob.Spec]);
        jobManager
            .Setup(m => m.GetExecutionAsync(parentExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);
        jobManager
            .Setup(m => m.QueryExecutionsAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [] });
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var parentScope = new Mock<IJobExecutionScope>();
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        jobScopeFactory.Setup(f => f.CreateScope(parentExecutionId)).Returns(parentScope.Object);
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            EventCatalog.Names.ContentHierarchyItemCreated,
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = contentHierarchyItemId.ToString("D"),
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null) with
        {
            JobExecutionId = parentExecutionId,
        };

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.JobType, Is.EqualTo("ContentMetadataRefreshItem"));
            Assert.That(request.EventTriggerId, Is.EqualTo(trigger.Id));
        }
    }

    [Test]
    public async Task ExecuteAsync_DispatchesMetadataRefreshTrigger_WhenCreationHasNoParentExecution()
    {
        // Arrange
        var contentHierarchyItemId = Guid.NewGuid();
        var trigger = DefaultJobTriggerCatalog.EventTriggers
            .Single(candidate => candidate.JobType == "ContentMetadataRefreshItem");
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([ContentMetadataRefreshItemJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.hierarchyItem.created",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = contentHierarchyItemId.ToString("D"),
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.JobType, Is.EqualTo("ContentMetadataRefreshItem"));
            Assert.That(request.Parameters!["contentHierarchyItemId"],
                Is.EqualTo(contentHierarchyItemId.ToString("D")));
            Assert.That(request.EventTriggerId, Is.EqualTo(trigger.Id));
            jobManager.Verify(m => m.GetExecutionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(null, "2030-01-01T06:00:00Z")]
    [TestCase("2", "2030-01-01T02:00:00Z")]
    public async Task ExecuteAsync_EnqueuesScopedLeafRetries_WhenCleanupCoalesces(
        string? configuredRetryCooldownHours,
        string expectedNotBefore)
    {
        // Arrange
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var firstDownloadId = Guid.NewGuid();
        var secondDownloadId = Guid.NewGuid();
        var firstParentHierarchyItemId = Guid.NewGuid();
        var firstHierarchyItemId = Guid.NewGuid();
        var secondHierarchyItemId = Guid.NewGuid();
        var firstContentItemId = Guid.NewGuid();
        var secondContentItemId = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        var retryCorrelationId = Guid.NewGuid();
        var coalescedCleanupExecutionId = Guid.NewGuid();
        var requests = new List<JobEnqueueRequest>();
        var requestsGate = new Lock();
        var requestsQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([DownloadCleanupJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                lock (requestsGate)
                {
                    requests.Add(request);
                    if (requests.Count(r => r.JobType == "ReleaseAutoGrabItem") >= 2 &&
                        requests.Count(r => r.JobType == "DownloadCleanup") == 2)
                    {
                        requestsQueued.TrySetResult();
                    }
                }
            })
            .Returns((JobEnqueueRequest request, CancellationToken _) => Task.FromResult(
                request.JobType == "DownloadCleanup"
                    ? coalescedCleanupExecutionId
                    : Guid.NewGuid()));

        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger>
            {
                Items =
                [
                    new EventTrigger
                    {
                        Id = Guid.NewGuid(),
                        JobType = "DownloadCleanup",
                        Scope = JobScope.Administrative,
                        EventFilter = new EventFilter(),
                        Name = "Download cleanup",
                        Enabled = true,
                    }
                ]
            });

        var scheduleTriggerRepository = new Mock<IScheduleTriggerRepository> { CallBase = true };
        scheduleTriggerRepository
            .Setup(r => r.QueryAsync(
                It.Is<ScheduleTriggerQuery>(query =>
                    query.JobType == "ReleaseAutoGrabScan" && query.Enabled == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ScheduleTrigger>
            {
                Items = configuredRetryCooldownHours is null
                    ? []
                    :
                    [
                        new ScheduleTrigger
                        {
                            Id = Guid.NewGuid(),
                            JobType = "ReleaseAutoGrabScan",
                            Scope = JobScope.Content,
                            Name = "Configured content auto-grab",
                            Schedule = "0 */6 * * *",
                            Enabled = true,
                            Parameters = new Dictionary<string, string>
                            {
                                ["retryCooldownHours"] =
                                    configuredRetryCooldownHours
                            },
                            CreatedAt = DateTime.UtcNow,
                        }
                    ]
            });

        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "system"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User { Id = systemUserId, Username = "system", ProviderType = "local" }],
                TotalItems = 1
            });

        var downloadScopeRepository = new Mock<IDownloadScopeRepository> { CallBase = true };
        downloadScopeRepository
            .Setup(r => r.QueryAsync(
                It.Is<DownloadScopeQuery>(query => query.DownloadId == firstDownloadId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<DownloadScope>
            {
                Items =
                [
                    new DownloadScope { ContentHierarchyItemId = firstParentHierarchyItemId },
                    new DownloadScope { ContentHierarchyItemId = firstHierarchyItemId },
                ],
                TotalItems = 2,
            });
        downloadScopeRepository
            .Setup(r => r.QueryAsync(
                It.Is<DownloadScopeQuery>(query => query.DownloadId == secondDownloadId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<DownloadScope>
            {
                Items = [new DownloadScope { ContentHierarchyItemId = secondHierarchyItemId }],
                TotalItems = 1,
            });

        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        contentHierarchyItemRepository
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 2 &&
                    ids.Contains(firstParentHierarchyItemId) &&
                    ids.Contains(firstHierarchyItemId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [firstParentHierarchyItemId] = 1 });
        contentHierarchyItemRepository
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(secondHierarchyItemId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var contentRepository = new Mock<IContentRepository> { CallBase = true };
        contentRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentItemQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.Count == 1 &&
                    query.ContentHierarchyItemIds.Contains(firstHierarchyItemId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem>
            {
                Items = [new ContentItem
                {
                    Id = firstContentItemId,
                    ContentHierarchyItemId = firstHierarchyItemId,
                    Fulfillment = FulfillmentStatus.Missing,
                }]
            });
        contentRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentItemQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.Count == 1 &&
                    query.ContentHierarchyItemIds.Contains(secondHierarchyItemId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem>
            {
                Items = [new ContentItem
                {
                    Id = secondContentItemId,
                    ContentHierarchyItemId = secondHierarchyItemId,
                    Fulfillment = FulfillmentStatus.Upgradable,
                }]
            });

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(scheduleTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(Mock.Of<IJobExecutionScopeFactory>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(downloadScopeRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(contentRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(contentHierarchyItemRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: clock,
            databaseMigration: _migrationState);

        await subject.StartAsync(default).ConfigureAwait(false);
        var firstEvent = new MagpieEvent(
            Guid.Empty,
            EventCatalog.Names.DownloadFailed,
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.DownloadId] = firstDownloadId.ToString("D", CultureInfo.InvariantCulture),
                [EventCatalog.Keys.DownloadReleaseId] = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                [EventCatalog.Keys.DownloadFailureReason] = DownloadFailureReason.DownloadErrored.ToString()
            },
            default,
            default,
            null)
        {
            CorrelationId = retryCorrelationId
        };
        var secondEvent = new MagpieEvent(
            Guid.Empty,
            EventCatalog.Names.DownloadFailed,
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.DownloadId] = secondDownloadId.ToString("D", CultureInfo.InvariantCulture),
                [EventCatalog.Keys.DownloadReleaseId] = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                [EventCatalog.Keys.DownloadFailureReason] = DownloadFailureReason.DownloadStuck.ToString()
            },
            default,
            default,
            null)
        {
            CorrelationId = retryCorrelationId
        };

        // Act
        await Task.WhenAll(eventBus.PublishAsync(firstEvent), eventBus.PublishAsync(secondEvent))
            .ConfigureAwait(false);
        await requestsQueued.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        JobEnqueueRequest[] capturedRequests;
        lock (requestsGate)
        {
            capturedRequests = [.. requests];
        }

        var retryRequests = capturedRequests.Where(r => r.JobType == "ReleaseAutoGrabItem").ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retryRequests, Has.Count.EqualTo(2));
            Assert.That(
                retryRequests.Select(r => r.Parameters!["contentItemId"]),
                Is.EquivalentTo([firstContentItemId.ToString(), secondContentItemId.ToString()]));
            Assert.That(retryRequests.All(r => r.UserId == systemUserId), Is.True);
            Assert.That(retryRequests.All(r => r.CorrelationId == retryCorrelationId), Is.True);
            Assert.That(retryRequests.Select(request => request.TriggerContext!.ToMagpieEvent()!.Name),
                Is.All.EqualTo("download.failed"));
            Assert.That(
                retryRequests.Select(request => request.NotBeforeAt),
                Is.All.EqualTo(DateTime.Parse(expectedNotBefore, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal)));
            Assert.That(capturedRequests.Count(r => r.JobType == "DownloadCleanup"), Is.EqualTo(2));
        }

        contentRepository.Verify(
            r => r.QueryAsync(
                It.Is<ContentItemQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.Contains(firstParentHierarchyItemId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SetsNotBeforeAt_WhenTriggerHasDelay()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Delayed",
            EventFilter = new EventFilter(),
            Enabled = true,
            DelaySeconds = 15,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider,
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>(),
            now.UtcDateTime,
            now.UtcDateTime,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(request.NotBeforeAt, Is.EqualTo(now.UtcDateTime.AddSeconds(15)));
    }

    [Test]
    public async Task ExecuteAsync_LeavesNotBeforeAtNull_WhenTriggerHasNoDelay()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "SomeJob",
            Scope = JobScope.Content,
            Name = "Immediate",
            EventFilter = new EventFilter(),
            Enabled = true,
            DelaySeconds = 0,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([
            new JobSpec
            {
                JobType = trigger.JobType,
                DisplayName = "Some job",
                Category = "Test",
                Scope = trigger.Scope,
            }
        ]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider,
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>(),
            now.UtcDateTime,
            now.UtcDateTime,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(request.NotBeforeAt, Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesReleaseSearchForTheRefreshedItem_WhenMetadataRefreshCompletes()
    {
        // Arrange
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([ReleaseSearchItemJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [.. DefaultJobTriggerCatalog.EventTriggers] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "metadata.refreshed",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = "b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.JobType, Is.EqualTo("ReleaseSearchItem"));
            Assert.That(
                request.Parameters!["contentHierarchyItemId"],
                Is.EqualTo("b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6"));
        }
    }

    [Test]
    public async Task ExecuteAsync_PersistsACancelledExecution_WhenTheRefreshedItemNeedsNoRelease()
    {
        // Arrange
        var contentHierarchyItemId = Guid.Parse("b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6");
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([ReleaseSearchItemJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var persistedCompletion = new TaskCompletionSource<JobExecution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionRepository = new Mock<IJobExecutionRepository> { CallBase = true };
        executionRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<JobExecution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<JobExecution, Guid?, CancellationToken>((execution, _, _) =>
            {
                if (execution.JobType == "ReleaseSearchItem")
                {
                    persistedCompletion.TrySetResult(execution);
                }
            })
            .ReturnsAsync((JobExecution execution, Guid? _, CancellationToken _) => execution);
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        contentHierarchyItemRepository
            .Setup(repository => repository.GetByIdAsync(contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = contentHierarchyItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "fulfilled",
                Name = "Fulfilled",
                ContentItem = new ContentItem { Fulfillment = FulfillmentStatus.Fulfilled },
            });
        contentHierarchyItemRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.Ids != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([contentHierarchyItemId]);
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [.. DefaultJobTriggerCatalog.EventTriggers] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(contentHierarchyItemRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(executionRepository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "metadata.refreshed",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = "b9a1c4d2-3e5f-4a67-8b90-c1d2e3f4a5b6"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var persisted = await persistedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted.JobType, Is.EqualTo("ReleaseSearchItem"));
            Assert.That(persisted.Status, Is.EqualTo(JobExecutionStatus.Cancelled));
            Assert.That(
                persisted.StatusMessage,
                Is.EqualTo("Skipped because the content hierarchy item needs no release."));
            Assert.That(persisted.EventTriggerId, Is.Not.Null);
            Assert.That(persisted.CompletedAt, Is.Not.Null);
        }

        jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.JobType == "ReleaseSearchItem"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesWishlistRefreshForTheNewWishlist_WhenAWishlistIsCreated()
    {
        // Arrange
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([WishlistRefreshItemJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [.. DefaultJobTriggerCatalog.EventTriggers] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "wishlist.created",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.WishlistId] = "0f2c9a51-7d64-4b18-9c33-5a6e8f0d2b47"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.JobType, Is.EqualTo("WishlistRefreshItem"));
            Assert.That(
                request.Parameters!["wishlistId"],
                Is.EqualTo("0f2c9a51-7d64-4b18-9c33-5a6e8f0d2b47"));
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesDownloadImportForTheCompletedDownload_WhenADownloadCompletes()
    {
        // Arrange
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([DownloadImportItemJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [.. DefaultJobTriggerCatalog.EventTriggers] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.DownloadId] = "3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.JobType, Is.EqualTo("DownloadImportItem"));
            Assert.That(
                request.Parameters!["downloadId"],
                Is.EqualTo("3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"));
            Assert.That(
                request.Parameters,
                Does.ContainKey(JobExecutionDedup.ParameterKey),
                "A repeated delivery of the same download.completed event must coalesce.");
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesAutoGrabForTheMatchedItem_WhenAReleaseMatchesTheSearchedItem()
    {
        // Arrange
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.GetSpecs(null)).Returns([ReleaseAutoGrabItemJob.Spec]);
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository> { CallBase = true };
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [.. DefaultJobTriggerCatalog.EventTriggers] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository> { CallBase = true };
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());
        var userRepository = new Mock<IUserRepository> { CallBase = true };
        userRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationProvider>)))
            .Returns(Array.Empty<INotificationProvider>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IEventTriggerRepository)))
            .Returns(eventTriggerRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IScheduleTriggerRepository)))
            .Returns(Mock.Of<IScheduleTriggerRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(INotificationRuleRepository)))
            .Returns(notificationRuleRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobManager)))
            .Returns(jobManager.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionScopeFactory)))
            .Returns(jobScopeFactory.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IUserRepository)))
            .Returns(userRepository.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IDownloadScopeRepository)))
            .Returns(Mock.Of<IDownloadScopeRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentRepository)))
            .Returns(Mock.Of<IContentRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IContentHierarchyItemRepository)))
            .Returns(Mock.Of<IContentHierarchyItemRepository>());
        serviceProvider
            .Setup(p => p.GetService(typeof(IJobExecutionRepository)))
            .Returns(Mock.Of<IJobExecutionRepository>());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var hostEnvironment = new MagpieHostEnvironment(
            "node-a",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        using var eventBus = new EventBus(hostEnvironment, jobScopeFactory.Object, TimeProvider.System);
        using var subject = new EventDispatchBackgroundService(
            eventBus,
            scopeFactory.Object,
            NullLogger<EventDispatchBackgroundService>.Instance,
            hostEnvironment,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow),
            databaseMigration: _migrationState);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "release.matched",
            new Dictionary<string, string>
            {
                [EventCatalog.Keys.ContentHierarchyItemId] = "c5d74fd4-8fe1-46de-8c36-6dbf1f513dac",
                [EventCatalog.Keys.ContentItemId] = "329e68ae-bac5-48d3-9ed3-981acf287bcf"
            },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await eventBus.PublishAsync(magpieEvent).ConfigureAwait(false);
        var request = await requestDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.JobType, Is.EqualTo("ReleaseAutoGrabItem"));
            Assert.That(
                request.Parameters!["contentItemId"],
                Is.EqualTo("329e68ae-bac5-48d3-9ed3-981acf287bcf"));
            Assert.That(
                request.Parameters,
                Does.ContainKey(JobExecutionDedup.ParameterKey),
                "A repeated delivery of the same release.matched event must coalesce.");
        }
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
}
