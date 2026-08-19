using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Observability;
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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Events;

internal sealed class EventDispatchBackgroundService(
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    ILogger<EventDispatchBackgroundService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> NonDispatchableEventNames =
    [
        MagpieEvents.Names.AuditRecordCreated,
        MagpieEvents.Names.JobExecutionCancellationRequested,
        MagpieEvents.Names.JobExecutionCancelled,
        MagpieEvents.Names.JobExecutionCompleted,
        MagpieEvents.Names.JobExecutionCreated,
        MagpieEvents.Names.JobExecutionProgressChanged,
        MagpieEvents.Names.JobExecutionQueued,
        MagpieEvents.Names.JobExecutionStarted,
    ];

    private readonly TaskCompletionSource _listenerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private HashSet<string> _notificationProviderTypes = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        await _listenerReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool Matches(EventFilter filter, MagpieEvent magpieEvent)
    {
        if (filter.EventName is not null && !MatchesString(magpieEvent.Name, filter.EventName))
        {
            return false;
        }

        if (filter.EventContext?.Items is null || filter.EventContext.Items.Count == 0)
        {
            return true;
        }

        foreach (var (key, criterion) in filter.EventContext.Items)
        {
            magpieEvent.Context.TryGetValue(key, out var value);
            if (!MatchesString(value, criterion))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDispatchable(MagpieEvent magpieEvent)
    {
        return !NonDispatchableEventNames.Contains(magpieEvent.Name);
    }

    private static string GetNamePrefix(string name)
    {
        var separator = name.IndexOf('.');
        return separator < 0 ? name : name[..separator];
    }

    private static bool MatchesString(string? actual, PropertyCriterion<string> criterion)
    {
        if (criterion.Operator == CriteriaValueOperator.Regex)
        {
            return MatchesRegexCriterion(actual, criterion);
        }

        var matched = MatchesOperator(actual, criterion);
        return criterion.Negate ? !matched : matched;
    }

    private static bool MatchesRegexCriterion(string? actual, PropertyCriterion<string> criterion)
    {
        if (actual is null || criterion.Value is null)
        {
            return criterion.Negate;
        }

        var matched = TryMatchRegex(actual, criterion.Value);
        if (matched is null)
        {
            return false;
        }

        return criterion.Negate ? !matched.Value : matched.Value;
    }

    private static bool MatchesOperator(string? actual, PropertyCriterion<string> criterion)
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => string.Equals(actual, criterion.Value, StringComparison.Ordinal),
            CriteriaValueOperator.NotEquals => !string.Equals(actual, criterion.Value, StringComparison.Ordinal),
            CriteriaValueOperator.IsNull => actual is null,
            CriteriaValueOperator.IsNotNull => actual is not null,
            CriteriaValueOperator.In => actual is not null &&
                criterion.Values is not null &&
                criterion.Values.Contains(actual, StringComparer.Ordinal),
            CriteriaValueOperator.NotIn => actual is null ||
                criterion.Values is null ||
                !criterion.Values.Contains(actual, StringComparer.Ordinal),
            CriteriaValueOperator.ContainsString => actual is not null &&
                criterion.Value is not null &&
                actual.Contains(criterion.Value, StringComparison.Ordinal),
            CriteriaValueOperator.NotContainsString => actual is null ||
                criterion.Value is null ||
                !actual.Contains(criterion.Value, StringComparison.Ordinal),
            CriteriaValueOperator.StartsWithString => actual is not null &&
                criterion.Value is not null &&
                actual.StartsWith(criterion.Value, StringComparison.Ordinal),
            CriteriaValueOperator.NotStartsWithString => actual is null ||
                criterion.Value is null ||
                !actual.StartsWith(criterion.Value, StringComparison.Ordinal),
            CriteriaValueOperator.EndsWithString => actual is not null &&
                criterion.Value is not null &&
                actual.EndsWith(criterion.Value, StringComparison.Ordinal),
            CriteriaValueOperator.NotEndsWithString => actual is null ||
                criterion.Value is null ||
                !actual.EndsWith(criterion.Value, StringComparison.Ordinal),
            var op => throw new NotSupportedException($"Event string filters do not support operator {op}.")
        };
    }

    private static bool? TryMatchRegex(string actual, string pattern)
    {
        try
        {
            return Regex.IsMatch(actual, pattern, RegexOptions.None, RegexMatchTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static bool IsAuthoritativeMetadataRefreshExecution(
        JobExecution parent,
        JobExecution candidate,
        Guid contentHierarchyItemId)
    {
        return parent.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
            parent.CorrelationId is not null &&
            candidate.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
            candidate.EventTriggerId is null &&
            candidate.ScheduleTriggerId is null &&
            candidate.ParentExecutionId == parent.Id &&
            candidate.CorrelationId == parent.CorrelationId &&
            candidate.CreatedByUserId == parent.CreatedByUserId &&
            candidate.ExecutionMode == JobExecutionMode.Queue &&
            candidate.Parameters.TryGetValue(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                out var rawContentHierarchyItemId) &&
            Guid.TryParse(rawContentHierarchyItemId, out var candidateContentHierarchyItemId) &&
            candidateContentHierarchyItemId == contentHierarchyItemId;
    }

    private static bool ShouldSuppressMetadataRefreshEventTrigger(
        EventTrigger trigger,
        Guid? authoritativeMetadataRefreshTarget)
    {
        return authoritativeMetadataRefreshTarget is { } contentHierarchyItemId &&
            trigger.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
            TargetsContentHierarchyItem(trigger, contentHierarchyItemId);
    }

    private static bool TryGetCreatedHierarchyItemId(MagpieEvent magpieEvent, out Guid contentHierarchyItemId)
    {
        contentHierarchyItemId = default;
        return magpieEvent.Name == MagpieEvents.Names.ContentHierarchyItemCreated &&
            magpieEvent.Context.TryGetValue(
                MagpieEvents.Keys.ContentHierarchyItemId,
                out var rawContentHierarchyItemId) &&
            Guid.TryParse(rawContentHierarchyItemId, out contentHierarchyItemId);
    }

    private static bool TargetsContentHierarchyItem(EventTrigger trigger, Guid contentHierarchyItemId)
    {
        if (!trigger.Parameters.TryGetValue(
            MagpieJobs.Parameters.ContentHierarchyItemId,
            out var rawContentHierarchyItemId) || string.IsNullOrWhiteSpace(rawContentHierarchyItemId))
        {
            return true;
        }

        if (rawContentHierarchyItemId.Contains("${", StringComparison.Ordinal))
        {
            return true;
        }

        return Guid.TryParse(rawContentHierarchyItemId, out var target) && target == contentHierarchyItemId;
    }

    private static bool TryGetDownloadId(MagpieEvent magpieEvent, out Guid downloadId)
    {
        downloadId = default;

        if (magpieEvent is null ||
            !string.Equals(magpieEvent.Name, MagpieEvents.Names.DownloadFailed, StringComparison.Ordinal) ||
            !magpieEvent.Context.TryGetValue(MagpieEvents.Keys.DownloadId, out var raw))
        {
            return false;
        }

        return Guid.TryParse(raw, out downloadId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using (var scope = scopeFactory.CreateScope())
            {
                _notificationProviderTypes =
                [..
                    from provider in scope.ServiceProvider.GetServices<INotificationProvider>()
                    where !string.IsNullOrWhiteSpace(provider.ProviderType)
                    select provider.ProviderType                                                ];
            }
        }
        catch (Exception ex)
        {
            _listenerReady.TrySetException(ex);
            throw;
        }

        try
        {
            using var listener = eventBus is EventBus bus
                ? bus.CreateListener(eventName: null, localOnly: true, callSite: "dispatch")
                : eventBus.CreateListener(localOnly: true);
            _listenerReady.TrySetResult();
            await foreach (var magpieEvent in listener.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!IsDispatchable(magpieEvent))
                {
                    continue;
                }

                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using var scopeHandle = scope.ConfigureAwait(false);
                    var eventTriggerRepository = scope.ServiceProvider.GetRequiredService<IEventTriggerRepository>();
                    var scheduleTriggerRepository =
                        scope.ServiceProvider.GetRequiredService<IScheduleTriggerRepository>();
                    var notificationRuleRepository =
                        scope.ServiceProvider.GetRequiredService<INotificationRuleRepository>();
                    var jobManager = scope.ServiceProvider.GetRequiredService<IJobManager>();
                    var jobScopeFactory = scope.ServiceProvider.GetRequiredService<IJobExecutionScopeFactory>();
                    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                    var downloadScopeRepository = scope.ServiceProvider.GetRequiredService<IDownloadScopeRepository>();
                    var contentRepository = scope.ServiceProvider.GetRequiredService<IContentRepository>();
                    var contentHierarchyItemRepository =
                        scope.ServiceProvider.GetRequiredService<IContentHierarchyItemRepository>();
                    var jobExecutionRepository = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
                    IJobExecutionScope? parentScope = null;
                    try
                    {
                        if (magpieEvent.JobExecutionId is { } parentExecutionId)
                        {
                            parentScope = jobScopeFactory.CreateScope(parentExecutionId);
                            parentScope.SetParentContext(parentExecutionId);
                        }

                        await DispatchAsync(
                            magpieEvent,
                            eventTriggerRepository,
                            scheduleTriggerRepository,
                            notificationRuleRepository,
                            jobManager,
                            userRepository,
                            downloadScopeRepository,
                            contentRepository,
                            contentHierarchyItemRepository,
                            jobExecutionRepository,
                            jobScopeFactory,
                            stoppingToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (parentScope is not null)
                        {
                            await parentScope.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The event dispatch service failed to run the triggers for event {EventName} ({EventId})",
                        magpieEvent.Name,
                        magpieEvent.Id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _listenerReady.TrySetException(ex);
            throw;
        }
    }

    private async Task DispatchAsync(
        MagpieEvent magpieEvent,
        IEventTriggerRepository eventTriggerRepository,
        IScheduleTriggerRepository scheduleTriggerRepository,
        INotificationRuleRepository notificationRuleRepository,
        IJobManager jobManager,
        IUserRepository userRepository,
        IDownloadScopeRepository downloadScopeRepository,
        IContentRepository contentRepository,
        IContentHierarchyItemRepository contentHierarchyItemRepository,
        IJobExecutionRepository jobExecutionRepository,
        IJobExecutionScopeFactory jobExecutionScopeFactory,
        CancellationToken cancellationToken)
    {
        EventsTelemetry.DispatchLatency.Record(
            Math.Max(0, (_time.GetUtcNow().UtcDateTime - magpieEvent.PublishedAt).TotalMilliseconds),
            new KeyValuePair<string, object?>("name_prefix", GetNamePrefix(magpieEvent.Name)));

        const int pageSize = 500;
        var triggers = new List<EventTrigger>();
        var pageNumber = 1;
        while (true)
        {
            var page = await eventTriggerRepository.QueryAsync(
                new EventTriggerQuery { Enabled = true },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            triggers.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        var rules = new List<NotificationRule>();
        pageNumber = 1;
        while (true)
        {
            var page = await notificationRuleRepository.QueryAsync(
                new NotificationRuleQuery { Enabled = true },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            rules.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        var systemUsers = await userRepository.QueryAsync(
            new UserQuery { Username = "system" },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        var systemUserId = systemUsers.Items.SingleOrDefault()?.Id;
        var authoritativeMetadataRefreshTarget = await ResolveAuthoritativeMetadataRefreshTargetAsync(
            magpieEvent,
            triggers,
            jobManager,
            cancellationToken)
            .ConfigureAwait(false);

        var requests = await CollectFailedDownloadRetryRequestsAsync(
            magpieEvent,
            scheduleTriggerRepository,
            downloadScopeRepository,
            contentRepository,
            contentHierarchyItemRepository,
            systemUserId,
            cancellationToken)
            .ConfigureAwait(false);
        var triggerFailures = new List<(EventTrigger Trigger, string Reason)>();
        requests.AddRange(CollectEventTriggerRequests(
            magpieEvent,
            triggers,
            systemUserId,
            authoritativeMetadataRefreshTarget,
            jobManager.GetSpecs(),
            triggerFailures));
        requests.AddRange(CollectNotificationRequests(magpieEvent, rules));

        foreach (var (trigger, reason) in triggerFailures)
        {
            try
            {
                await RecordEventTriggerFailureAsync(
                    trigger,
                    magpieEvent,
                    systemUserId,
                    reason,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The event dispatch service failed to record the failure of event trigger {TriggerId}",
                    trigger.Id);
            }
        }

        if (requests.Count == 0)
        {
            return;
        }

        foreach (var request in requests)
        {
            try
            {
                await jobManager.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The event dispatch service failed to enqueue job {JobType} for event {EventName} ({EventId})",
                    request.JobType,
                    magpieEvent.Name,
                    magpieEvent.Id);
            }
        }

        async Task RecordEventTriggerFailureAsync(
            EventTrigger trigger,
            MagpieEvent failedEvent,
            Guid? userId,
            string reason,
            CancellationToken token)
        {
            var executionId = Guid.NewGuid();
            var failedAt = _time.GetUtcNow().UtcDateTime;
            var failed = new JobExecution
            {
                Id = executionId,
                ExtensionId = trigger.ExtensionId,
                EventTriggerId = trigger.Id,
                JobType = trigger.JobType,
                Scope = trigger.Scope,
                Parameters = new Dictionary<string, string>(trigger.Parameters),
                TriggerContext = failedEvent.ToTriggerContext(),
                ExecutionMode = JobExecutionMode.Queue,
                Status = JobExecutionStatus.Failed,
                StatusMessage = reason,
                CompletedAt = failedAt,
                CorrelationId = Guid.NewGuid(),
                ParentExecutionId = jobExecutionScopeFactory.CurrentExecutionId,
                DedupKey = failedEvent.Id == Guid.Empty
                    ? null
                    : $"event-trigger:{trigger.Id:N}:{failedEvent.Id:N}",
            };

            var created = await jobExecutionRepository.CreateAsync(failed, userId, token).ConfigureAwait(false);
            if (created.Id != executionId)
            {
                logger.LogDebug(
                    "The event dispatch service skipped duplicate failure for event trigger {TriggerId} and event {EventId}",
                    trigger.Id,
                    failedEvent.Id);
                return;
            }

            JobsTelemetry.FailuresTotal.Add(
                1,
                new KeyValuePair<string, object?>("job.type", trigger.JobType),
                new KeyValuePair<string, object?>("reason_kind", "event_binding"));
        }
    }

    private async Task<List<JobEnqueueRequest>> CollectFailedDownloadRetryRequestsAsync(
        MagpieEvent magpieEvent,
        IScheduleTriggerRepository scheduleTriggerRepository,
        IDownloadScopeRepository downloadScopeRepository,
        IContentRepository contentRepository,
        IContentHierarchyItemRepository contentHierarchyItemRepository,
        Guid? systemUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetDownloadId(magpieEvent, out var downloadId))
        {
            return [];
        }

        try
        {
            const int pageSize = 500;
            var hierarchyItemIds = new HashSet<Guid>();
            var pageNumber = 1;
            while (true)
            {
                var page = await downloadScopeRepository.QueryAsync(
                    new DownloadScopeQuery { DownloadId = downloadId },
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                hierarchyItemIds.UnionWith(pageItems.Select(scope => scope.ContentHierarchyItemId));
                if (pageItems.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }

            if (hierarchyItemIds.Count == 0)
            {
                return [];
            }

            var childCounts = await contentHierarchyItemRepository
                .GetChildCountsByParentAsync(hierarchyItemIds, cancellationToken)
                .ConfigureAwait(false);
            var leafHierarchyItemIds = hierarchyItemIds
                .Where(hierarchyItemId =>
                    !childCounts.TryGetValue(hierarchyItemId, out var childCount) || childCount == 0)
                .ToList();
            if (leafHierarchyItemIds.Count == 0)
            {
                return [];
            }

            var contentResults = await contentRepository.QueryAsync(
                new() { ContentHierarchyItemIds = leafHierarchyItemIds },
                new() { PageNumber = 1, PageSize = Math.Max(1, leafHierarchyItemIds.Count) },
                null,
                cancellationToken).ConfigureAwait(false);
            var retryCooldown = await ResolveFailedDownloadRetryCooldownAsync(
                scheduleTriggerRepository,
                cancellationToken)
                .ConfigureAwait(false);
            var notBeforeAt = _time.GetUtcNow().UtcDateTime.Add(retryCooldown);
            return
            [
                .. contentResults.Items
                    .Where(contentItem =>
                        contentItem.Fulfillment is FulfillmentStatus.Missing or FulfillmentStatus.Upgradable)
                    .Select(contentItem => new JobEnqueueRequest
                    {
                        JobType = ReleaseAutoGrabItemJob.Spec.JobType,
                        Parameters = new Dictionary<string, string>
                        {
                            [MagpieJobs.Parameters.ContentItemId] = contentItem.Id.ToString()
                        },
                        Mode = JobExecutionMode.Queue,
                        UserId = systemUserId,
                        NotBeforeAt = notBeforeAt,
                    })
            ];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The event dispatch service failed to collect the alternate-release retries " +
                "for failed download {DownloadId} and scheduled no retry",
                downloadId);
            return [];
        }
    }

    private async Task<TimeSpan> ResolveFailedDownloadRetryCooldownAsync(
        IScheduleTriggerRepository scheduleTriggerRepository,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var pageNumber = 1;
        ScheduleTrigger? trigger = null;
        while (trigger is null)
        {
            var page = await scheduleTriggerRepository.QueryAsync(
                new ScheduleTriggerQuery
                {
                    JobType = ReleaseAutoGrabScanJob.Spec.JobType,
                    Enabled = true,
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                new()
                {
                    PropertyName = nameof(ScheduleTrigger.CreatedAt),
                    Direction = SortDirection.Ascending,
                },
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            trigger = pageItems.FirstOrDefault(candidate =>
                candidate.Parameters.ContainsKey(MagpieJobs.Parameters.RetryCooldownHours));
            if (trigger is not null || pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        if (trigger?.Parameters.TryGetValue(
            MagpieJobs.Parameters.RetryCooldownHours,
            out var raw) is true)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) &&
                configured >= 0)
            {
                return TimeSpan.FromHours(configured);
            }

            logger.LogWarning(
                "Retry cooldown {Raw} for the auto-grab scan is not a non-negative integer. " +
                "The event dispatch service will use the default value {Default}",
                raw,
                MagpieJobs.ParameterValues.DefaultRetryCooldownHours);
        }

        return TimeSpan.FromHours(MagpieJobs.ParameterValues.DefaultRetryCooldownHours);
    }

    internal async Task<Guid?> ResolveAuthoritativeMetadataRefreshTargetAsync(
        MagpieEvent magpieEvent,
        IEnumerable<EventTrigger> triggers,
        IJobManager jobManager,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreatedHierarchyItemId(magpieEvent, out var contentHierarchyItemId) ||
            magpieEvent.JobExecutionId is not { } parentExecutionId ||
            !triggers.Any(trigger =>
                trigger.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
                TargetsContentHierarchyItem(trigger, contentHierarchyItemId)))
        {
            return null;
        }

        try
        {
            var parent = await jobManager.GetExecutionAsync(parentExecutionId, cancellationToken)
                .ConfigureAwait(false);
            if (parent is null ||
                parent.JobType != ContentMetadataRefreshItemJob.Spec.JobType ||
                parent.CorrelationId is null)
            {
                return null;
            }

            const int pageSize = 500;
            var pageNumber = 1;
            while (true)
            {
                var page = await jobManager.QueryExecutionsAsync(
                    new JobExecutionQuery
                    {
                        JobType = ContentMetadataRefreshItemJob.Spec.JobType,
                        CorrelationId = parent.CorrelationId,
                        ParameterValueReferences =
                            [
                                contentHierarchyItemId.ToString("D", CultureInfo.InvariantCulture)
                            ],
                    },
                    new PagingParameter { PageNumber = pageNumber, PageSize = pageSize },
                    sort: null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                if (pageItems.Any(candidate => IsAuthoritativeMetadataRefreshExecution(
                    parent,
                    candidate,
                    contentHierarchyItemId)))
                {
                    return contentHierarchyItemId;
                }

                if (pageItems.Count < pageSize)
                {
                    return null;
                }

                pageNumber++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The event dispatch service failed to resolve the authoritative metadata refresh " +
                "for content hierarchy item {ContentHierarchyItemId} and will use the event trigger",
                contentHierarchyItemId);
            return null;
        }
    }

    private List<JobEnqueueRequest> CollectEventTriggerRequests(
        MagpieEvent magpieEvent,
        IEnumerable<EventTrigger> triggers,
        Guid? systemUserId,
        Guid? authoritativeMetadataRefreshTarget,
        IReadOnlyList<JobSpec> specs,
        ICollection<(EventTrigger Trigger, string Reason)> failures)
    {
        var requests = new List<JobEnqueueRequest>();
        foreach (var trigger in triggers)
        {
            try
            {
                if (!Matches(trigger.EventFilter, magpieEvent))
                {
                    continue;
                }

                if (ShouldSuppressMetadataRefreshEventTrigger(trigger, authoritativeMetadataRefreshTarget))
                {
                    continue;
                }

                var parameters = ResolveEventParameters(trigger.Parameters, magpieEvent.Context);

                // Use a linear lookup because duplicate plugin job types are possible.
                var spec = specs.FirstOrDefault(candidate => candidate.JobType == trigger.JobType);
                if (spec is not null)
                {
                    ValidateResolvedParameterTypes(spec, parameters);
                }

                var dedupKey = magpieEvent.Id == Guid.Empty
                    ? null
                    : $"event-trigger:{trigger.Id:N}:{magpieEvent.Id:N}";
                if (dedupKey is not null)
                {
                    parameters[JobExecutionDedup.ParameterKey] = dedupKey;
                }

                var notBeforeAt = trigger.DelaySeconds > 0
                    ? _time.GetUtcNow().UtcDateTime.AddSeconds(trigger.DelaySeconds)
                    : (DateTime?)null;

                requests.Add(new JobEnqueueRequest
                {
                    JobType = trigger.JobType,
                    ExtensionId = trigger.ExtensionId,
                    EventTriggerId = trigger.Id,
                    Parameters = parameters,
                    TriggerContext = magpieEvent.ToTriggerContext(),
                    Mode = JobExecutionMode.Queue,
                    UserId = systemUserId,
                    NotBeforeAt = notBeforeAt,
                });
            }
            catch (MagpieException ex)
            {
                failures.Add((trigger, ex.Message));
                logger.LogError(
                    ex,
                    "Event trigger {TriggerId} failed for event {EventName} ({EventId})",
                    trigger.Id,
                    magpieEvent.Name,
                    magpieEvent.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "An unhandled error stopped event trigger {TriggerId} for event {EventName} ({EventId})",
                    trigger.Id,
                    magpieEvent.Name,
                    magpieEvent.Id);
            }
        }

        return requests;
    }

    internal static string ResolveEventParameter(
        string expression,
        IReadOnlyDictionary<string, string> eventContext)
    {
        return TransformEventParameter(
            expression,
            key => eventContext.TryGetValue(key, out var value)
                ? value
                : throw new MagpieException($"Event context key {key} is missing."));
    }

    internal static Dictionary<string, string> ResolveEventParameters(
        Dictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> eventContext)
    {
        var result = new Dictionary<string, string>(parameters.Count, parameters.Comparer);
        foreach (var (name, value) in parameters)
        {
            result[name] = ResolveEventParameter(value, eventContext);
        }

        return result;
    }

    internal static void ValidateResolvedParameterTypes(
        JobSpec spec,
        IReadOnlyDictionary<string, string> parameters)
    {
        foreach (var parameter in spec.Parameters)
        {
            if (!parameters.TryGetValue(parameter.Name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var valid = parameter.Type switch
            {
                DataType.String => true,
                DataType.Number => double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number) && double.IsFinite(number),
                DataType.Boolean => bool.TryParse(value, out _),
                DataType.Date => DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _),
                _ => true,
            };
            if (!valid)
            {
                var typeName = parameter.Type.ToString().ToLowerInvariant();
                throw new MagpieException($"Parameter {parameter.Name} must be a valid {typeName}.");
            }
        }
    }

    private static string TransformEventParameter(
        string expression,
        Func<string, string> resolve)
    {
        var result = new StringBuilder(expression.Length);
        for (var index = 0; index < expression.Length;)
        {
            if (expression.AsSpan(index).StartsWith("$${", StringComparison.Ordinal))
            {
                result.Append("${");
                index += 3;
                continue;
            }

            if (expression.AsSpan(index).StartsWith("${", StringComparison.Ordinal))
            {
                const string prefix = "${event.";
                if (!expression.AsSpan(index).StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw new MagpieException("Event binding is malformed.");
                }

                var keyStart = index + prefix.Length;
                var end = keyStart;
                while (end < expression.Length && expression[end] != '}')
                {
                    if (expression[end] == '{')
                    {
                        throw new MagpieException("Event binding is malformed.");
                    }

                    end++;
                }

                if (end == expression.Length || end == keyStart)
                {
                    throw new MagpieException("Event binding is malformed.");
                }

                result.Append(resolve(expression[keyStart..end]));
                index = end + 1;
                continue;
            }

            result.Append(expression[index]);
            index++;
        }

        return result.ToString();
    }

    private List<JobEnqueueRequest> CollectNotificationRequests(
        MagpieEvent magpieEvent,
        IEnumerable<NotificationRule> rules)
    {
        var requests = new List<JobEnqueueRequest>();
        foreach (var rule in rules)
        {
            bool matched;
            try
            {
                matched = Matches(rule.EventFilter, magpieEvent);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The filter of notification rule {RuleName} ({RuleId}) failed for event {EventName}. " +
                    "The event dispatch service skipped the rule",
                    rule.Name,
                    rule.Id,
                    magpieEvent.Name);
                continue;
            }

            if (!matched)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.ProviderType) || !_notificationProviderTypes.Contains(rule.ProviderType))
            {
                logger.LogWarning(
                    "Notification rule {RuleName} ({RuleId}) references unknown provider type " +
                    "{ProviderType}. The event dispatch service skipped the rule",
                    rule.Name,
                    rule.Id,
                    rule.ProviderType);
                continue;
            }

            var parameters = new Dictionary<string, string>()
            {
                [MagpieJobs.Parameters.RuleId] = rule.Id.ToString("D"),
                [MagpieJobs.Parameters.Event] = JsonSerializer.Serialize(magpieEvent, _serializerOptions),
            };
            var dedupKey = magpieEvent.Id == Guid.Empty
                ? null
                : $"notification:{rule.Id:N}:{magpieEvent.Id:N}";
            if (dedupKey is not null)
            {
                parameters[JobExecutionDedup.ParameterKey] = dedupKey;
            }

            requests.Add(new JobEnqueueRequest
            {
                JobType = SendNotificationJob.Spec.JobType,
                Parameters = parameters,
                Mode = JobExecutionMode.Queue,
            });
        }

        return requests;
    }
}
