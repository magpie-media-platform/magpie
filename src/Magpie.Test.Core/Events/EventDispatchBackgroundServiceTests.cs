using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Services;

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
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger, sentinelTrigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            NullLogger<EventDispatchBackgroundService>.Instance);
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
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger, sentinelTrigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            NullLogger<EventDispatchBackgroundService>.Instance);
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
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            NullLogger<EventDispatchBackgroundService>.Instance);
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
            JobType = ContentMetadataRefreshItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Parameters = new Dictionary<string, string>
            {
                [MagpieJobs.Parameters.ContentHierarchyItemId] = Guid.NewGuid().ToString("D")
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
            JobType = ContentMetadataRefreshItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Parameters = new Dictionary<string, string>
            {
                [MagpieJobs.Parameters.ContentHierarchyItemId] =
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
            JobType = ContentMetadataRefreshItemJob.Spec.JobType,
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
        jobManager
            .Setup(m => m.GetExecutionAsync(parentExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);
        jobManager
            .Setup(m => m.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
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
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
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
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            NullLogger<EventDispatchBackgroundService>.Instance);
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
            JobType = ContentMetadataRefreshItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Parameters = new Dictionary<string, string>
            {
                [MagpieJobs.Parameters.ContentHierarchyItemId] = Guid.NewGuid().ToString("D")
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
            JobType = ContentMetadataRefreshItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Refresh created content hierarchy item",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
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
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
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
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            NullLogger<EventDispatchBackgroundService>.Instance);
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
            Assert.That(request.JobType, Is.EqualTo(ContentMetadataRefreshItemJob.Spec.JobType));
            Assert.That(request.EventTriggerId, Is.EqualTo(trigger.Id));
        }
    }

    [Test]
    public async Task ExecuteAsync_DispatchesMetadataRefreshTrigger_WhenCreationHasNoParentExecution()
    {
        // Arrange
        var contentHierarchyItemId = Guid.NewGuid();
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            JobType = ContentMetadataRefreshItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Refresh created content hierarchy item",
            EventFilter = new EventFilter(),
            Enabled = true,
        };
        var requestDispatched = new TaskCompletionSource<JobEnqueueRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            NullLogger<EventDispatchBackgroundService>.Instance);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            EventCatalog.Names.ContentHierarchyItemCreated,
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
            Assert.That(request.JobType, Is.EqualTo(ContentMetadataRefreshItemJob.Spec.JobType));
            Assert.That(request.EventTriggerId, Is.EqualTo(trigger.Id));
            jobManager.Verify(m => m.GetExecutionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(null, MagpieJobs.ParameterValues.DefaultRetryCooldownHours)]
    [TestCase("2", 2)]
    public async Task ExecuteAsync_EnqueuesScopedLeafRetries_WhenCleanupCoalesces(
        string? configuredRetryCooldownHours,
        int expectedRetryCooldownHours)
    {
        // Arrange
        var firstDownloadId = Guid.NewGuid();
        var secondDownloadId = Guid.NewGuid();
        var firstParentHierarchyItemId = Guid.NewGuid();
        var firstHierarchyItemId = Guid.NewGuid();
        var secondHierarchyItemId = Guid.NewGuid();
        var firstContentItemId = Guid.NewGuid();
        var secondContentItemId = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        var coalescedCleanupExecutionId = Guid.NewGuid();
        var requests = new List<JobEnqueueRequest>();
        var requestsGate = new Lock();
        var requestsQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) =>
            {
                lock (requestsGate)
                {
                    requests.Add(request);
                    if (requests.Count(r => r.JobType == "ReleaseAutoGrabItem") >= 2 &&
                        requests.Count(r => r.JobType == DownloadCleanupJob.Spec.JobType) == 2)
                    {
                        requestsQueued.TrySetResult();
                    }
                }
            })
            .Returns((JobEnqueueRequest request, CancellationToken _) => Task.FromResult(
                request.JobType == DownloadCleanupJob.Spec.JobType
                    ? coalescedCleanupExecutionId
                    : Guid.NewGuid()));

        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
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
                        JobType = DownloadCleanupJob.Spec.JobType,
                        Scope = JobScope.Administrative,
                        EventFilter = new EventFilter(),
                        Name = "Download cleanup",
                        Enabled = true,
                    }
                ]
            });

        var scheduleTriggerRepository = new Mock<IScheduleTriggerRepository>();
        scheduleTriggerRepository
            .Setup(r => r.QueryAsync(
                It.Is<ScheduleTriggerQuery>(query =>
                    query.JobType == ReleaseAutoGrabScanJob.Spec.JobType && query.Enabled == true),
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
                            JobType = ReleaseAutoGrabScanJob.Spec.JobType,
                            Scope = JobScope.Content,
                            Name = "Configured content auto-grab",
                            Schedule = "0 */6 * * *",
                            Enabled = true,
                            Parameters = new Dictionary<string, string>
                            {
                                [MagpieJobs.Parameters.RetryCooldownHours] =
                                    configuredRetryCooldownHours
                            },
                            CreatedAt = DateTime.UtcNow,
                        }
                    ]
            });

        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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

        var downloadScopeRepository = new Mock<IDownloadScopeRepository>();
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

        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
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

        var contentRepository = new Mock<IContentRepository>();
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
            NullLogger<EventDispatchBackgroundService>.Instance);

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
            null);
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
            null);
        var retryWindowStartedAt = DateTime.UtcNow;

        // Act
        await Task.WhenAll(eventBus.PublishAsync(firstEvent), eventBus.PublishAsync(secondEvent))
            .ConfigureAwait(false);
        await requestsQueued.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var retryWindowEndedAt = DateTime.UtcNow;
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
            Assert.That(
                retryRequests.All(request =>
                    request.NotBeforeAt is { } notBeforeAt &&
                    notBeforeAt >= retryWindowStartedAt.AddHours(expectedRetryCooldownHours) &&
                    notBeforeAt <= retryWindowEndedAt.AddHours(expectedRetryCooldownHours)),
                Is.True);
            Assert.That(capturedRequests.Count(r => r.JobType == DownloadCleanupJob.Spec.JobType), Is.EqualTo(2));
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
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            timeProvider);
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
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            timeProvider);
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
    public async Task ExecuteAsync_RetainsDeferral_WhenCoalescing()
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
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => requestDispatched.TrySetResult(request))
            .ReturnsAsync(Guid.NewGuid());
        var eventTriggerRepository = new Mock<IEventTriggerRepository>();
        eventTriggerRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<EventTriggerQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<EventTrigger> { Items = [trigger] });
        var jobScopeFactory = new Mock<IJobExecutionScopeFactory>();
        var notificationRuleRepository = new Mock<INotificationRuleRepository>();
        notificationRuleRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<NotificationRuleQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<NotificationRule>());

        var userRepository = new Mock<IUserRepository>();
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
            timeProvider);
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
        Assert.That(request.ClearDeferralOnCoalesce, Is.False);
    }
}
