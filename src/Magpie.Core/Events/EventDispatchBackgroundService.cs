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
    MagpieHostEnvironment hostEnvironment,
    TimeProvider timeProvider,
    DatabaseMigrationState databaseMigration) : BackgroundService
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

    private const string SuppressedReleaseSearchReason =
        "Skipped because the content hierarchy item needs no release.";

    private static readonly string EventTriggerNamePrefix = GetNamePrefix(MagpieEvents.Names.EventTriggerCreated);

    private static readonly string NotificationRuleNamePrefix =
        GetNamePrefix(MagpieEvents.Names.NotificationRuleCreated);

    private static readonly string UserNamePrefix = GetNamePrefix(MagpieEvents.Names.UserCreated);

    private readonly TaskCompletionSource _listenerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _time = timeProvider;
    private IReadOnlyList<EventTrigger>? _triggers;
    private IReadOnlyList<NotificationRule>? _rules;
    private Guid? _systemUserId;
    private bool _systemUserLoaded;

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

    private static bool IsNotificationDeliveryFailure(MagpieEvent magpieEvent)
    {
        return string.Equals(magpieEvent.Name, MagpieEvents.Names.JobExecutionFailed, StringComparison.Ordinal) &&
            magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExecutionJobType, out var jobType) &&
            string.Equals(jobType, MagpieJobs.Types.SendNotification, StringComparison.OrdinalIgnoreCase);
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
        return parent.JobType == MagpieJobs.Types.ContentMetadataRefreshItem &&
            parent.CorrelationId is not null &&
            candidate.JobType == MagpieJobs.Types.ContentMetadataRefreshItem &&
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
            trigger.JobType == MagpieJobs.Types.ContentMetadataRefreshItem &&
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

    private static Task InvokeForeignExtensionLifecycleAsync(
        MagpieEvent magpieEvent,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return magpieEvent.Name switch
        {
            MagpieEvents.Names.ExtensionDisabled => InvokeForeignDisableAsync(
                magpieEvent,
                services.GetRequiredService<IExtensionLifecycleManager>(),
                services.GetRequiredService<IExtensionRepository>(),
                cancellationToken),
            MagpieEvents.Names.ExtensionUninstalled => InvokeForeignUninstallAsync(
                magpieEvent,
                services.GetRequiredService<IExtensionLifecycleManager>(),
                cancellationToken),
            _ => Task.CompletedTask,
        };
    }

    private static async Task InvokeForeignDisableAsync(
        MagpieEvent magpieEvent,
        IExtensionLifecycleManager lifecycleManager,
        IExtensionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionId, out var rawId) ||
            !Guid.TryParse(rawId, out var extensionId))
        {
            return;
        }

        var extension = await repository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (extension is not null)
        {
            await lifecycleManager.InvokeDisableAsync(extension, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InvokeForeignUninstallAsync(
        MagpieEvent magpieEvent,
        IExtensionLifecycleManager lifecycleManager,
        CancellationToken cancellationToken)
    {
        magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionAssemblyName, out var assemblyName);
        magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionFilePath, out var filePath);
        if (string.IsNullOrWhiteSpace(assemblyName) && string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionId, out var rawId);
        _ = Guid.TryParse(rawId, out var extensionId);

        var extension = new Extension
        {
            Id = extensionId,
            PackageId = assemblyName ?? string.Empty,
            Name = assemblyName ?? string.Empty,
            Version = string.Empty,
            AssemblyName = assemblyName ?? string.Empty,
            FilePath = filePath ?? string.Empty,
        };
        await lifecycleManager.InvokeUninstallAsync(extension, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var listener = eventBus is EventBus bus
                ? bus.CreateListener(eventName: null, localOnly: false, callSite: "dispatch")
                : eventBus.CreateListener(localOnly: false);
            _listenerReady.TrySetResult();
            await databaseMigration.Completion.WaitAsync(stoppingToken).ConfigureAwait(false);

            await foreach (var magpieEvent in listener.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                var namePrefix = GetNamePrefix(magpieEvent.Name);
                if (namePrefix == EventTriggerNamePrefix)
                {
                    _triggers = null;
                }

                if (namePrefix == NotificationRuleNamePrefix)
                {
                    _rules = null;
                }

                if (namePrefix == UserNamePrefix)
                {
                    _systemUserId = null;
                    _systemUserLoaded = false;
                }

                if (!IsDispatchable(magpieEvent))
                {
                    continue;
                }

                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using var scopeHandle = scope.ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(magpieEvent.NodeId) && magpieEvent.NodeId != hostEnvironment.NodeId)
                    {
                        if (hostEnvironment.IsClustered)
                        {
                            await InvokeForeignExtensionLifecycleAsync(
                                magpieEvent,
                                scope.ServiceProvider,
                                stoppingToken).ConfigureAwait(false);
                        }

                        continue;
                    }

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
                    var notificationProviders = scope.ServiceProvider.GetServices<INotificationProvider>();
                    IJobExecutionScope? parentScope = null;
                    try
                    {
                        if (magpieEvent.JobExecutionId is { } parentExecutionId)
                        {
                            parentScope = jobScopeFactory.CreateScope(parentExecutionId);
                            parentScope.SetParentContext(parentExecutionId, magpieEvent.CorrelationId);
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
                            notificationProviders,
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
        IEnumerable<INotificationProvider> notificationProviders,
        CancellationToken cancellationToken)
    {
        EventsTelemetry.DispatchLatency.Record(
            Math.Max(0, (_time.GetUtcNow().UtcDateTime - magpieEvent.PublishedAt).TotalMilliseconds),
            new KeyValuePair<string, object?>("name_prefix", GetNamePrefix(magpieEvent.Name)));

        if (_triggers is null)
        {
            var loadedTriggers = await eventTriggerRepository.QueryAllAsync(
                new EventTriggerQuery { Enabled = true },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _triggers = loadedTriggers;
        }

        if (_rules is null)
        {
            var loadedRules = await notificationRuleRepository.QueryAllAsync(
                new NotificationRuleQuery { Enabled = true },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _rules = loadedRules;
        }

        if (!_systemUserLoaded)
        {
            var systemUsers = await userRepository.QueryAsync(
                new UserQuery { Username = "system" },
                new() { PageNumber = 1, PageSize = 1 },
                null,
                cancellationToken).ConfigureAwait(false);
            _systemUserId = systemUsers.Items.SingleOrDefault()?.Id;
            _systemUserLoaded = true;
        }

        var systemUserId = _systemUserId;
        var rules = _rules;
        var triggers = new List<EventTrigger>(_triggers);
        var authoritativeMetadataRefreshTarget = await ResolveAuthoritativeMetadataRefreshTargetAsync(
            magpieEvent,
            triggers,
            jobManager,
            cancellationToken).ConfigureAwait(false);
        triggers.RemoveAll(trigger =>
            ShouldSuppressMetadataRefreshEventTrigger(trigger, authoritativeMetadataRefreshTarget));

        var correlationId = magpieEvent.CorrelationId;
        var triggerFailures = new List<(EventTrigger Trigger, string Reason)>();
        var requests = CollectEventTriggerRequests(
            magpieEvent,
            triggers,
            correlationId,
            systemUserId,
            jobManager.GetSpecs(),
            triggerFailures);
        var triggerOutcomes = triggerFailures
            .Select(failure => (failure.Trigger, Status: JobExecutionStatus.Failed, failure.Reason))
            .ToList();

        if (magpieEvent.Name == MagpieEvents.Names.ContentFulfillmentChanged ||
            requests.Exists(request => request.JobType == MagpieJobs.Types.ReleaseSearchItem))
        {
            var releaseSearchTarget = await ResolveAutomaticReleaseSearchTargetAsync(
                magpieEvent,
                contentHierarchyItemRepository,
                cancellationToken).ConfigureAwait(false);
            if (releaseSearchTarget is { NeedsSearch: false } fulfilledTarget)
            {
                if (magpieEvent.Name == MagpieEvents.Names.ContentFulfillmentChanged)
                {
                    await CancelAutomaticReleaseSearchesAsync(
                        fulfilledTarget.Id,
                        jobManager,
                        jobExecutionRepository,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (magpieEvent.Name == MagpieEvents.Names.MetadataRefreshed)
                {
                    var droppedTriggerIds = requests
                        .Where(request => IsUnforcedReleaseSearchFor(request, fulfilledTarget.Id))
                        .Select(request => request.EventTriggerId)
                        .ToList();
                    if (droppedTriggerIds.Count > 0)
                    {
                        requests.RemoveAll(
                            request => IsUnforcedReleaseSearchFor(request, fulfilledTarget.Id));
                        logger.LogInformation(
                            "The event dispatch service dropped {DroppedCount} automatic release searches for " +
                            "content hierarchy item {ContentHierarchyItemId} because the item needs no release",
                            droppedTriggerIds.Count,
                            fulfilledTarget.Id);
                        triggerOutcomes.AddRange(triggers
                            .Where(trigger => droppedTriggerIds.Contains(trigger.Id))
                            .Select(trigger => (
                                Trigger: trigger,
                                Status: JobExecutionStatus.Cancelled,
                                Reason: SuppressedReleaseSearchReason)));
                    }
                }
            }
        }

        requests.InsertRange(0, await CollectFailedDownloadRetryRequestsAsync(
            magpieEvent,
            correlationId,
            scheduleTriggerRepository,
            downloadScopeRepository,
            contentRepository,
            contentHierarchyItemRepository,
            systemUserId,
            cancellationToken).ConfigureAwait(false));
        requests.AddRange(CollectNotificationRequests(
            magpieEvent,
            correlationId,
            rules,
            notificationProviders));

        foreach (var (trigger, status, reason) in triggerOutcomes)
        {
            try
            {
                await RecordEventTriggerOutcomeAsync(
                    trigger,
                    magpieEvent,
                    systemUserId,
                    status,
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
                    "The event dispatch service failed to record the outcome of event trigger {TriggerId}",
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

        async Task RecordEventTriggerOutcomeAsync(
            EventTrigger trigger,
            MagpieEvent triggeringEvent,
            Guid? userId,
            JobExecutionStatus status,
            string reason,
            CancellationToken token)
        {
            var executionId = Guid.NewGuid();
            var completedAt = _time.GetUtcNow().UtcDateTime;
            var outcome = new JobExecution
            {
                Id = executionId,
                ExtensionId = trigger.ExtensionId,
                EventTriggerId = trigger.Id,
                JobType = trigger.JobType,
                Scope = trigger.Scope,
                Parameters = new Dictionary<string, string>(trigger.Parameters),
                TriggerContext = triggeringEvent.ToTriggerContext(),
                ExecutionMode = JobExecutionMode.Queue,
                Status = status,
                StatusMessage = reason,
                CompletedAt = completedAt,
                CorrelationId = correlationId ?? Guid.NewGuid(),
                ParentExecutionId = jobExecutionScopeFactory.CurrentExecutionId,
                DedupKey = triggeringEvent.Id == Guid.Empty
                    ? null
                    : $"event-trigger:{trigger.Id:N}:{triggeringEvent.Id:N}",
            };

            var created = await jobExecutionRepository.CreateAsync(outcome, userId, token).ConfigureAwait(false);
            if (created.Id != executionId)
            {
                logger.LogDebug(
                    "The event dispatch service skipped duplicate outcome for event trigger {TriggerId} " +
                    "and event {EventId}",
                    trigger.Id,
                    triggeringEvent.Id);
                return;
            }

            if (status != JobExecutionStatus.Failed)
            {
                return;
            }

            JobsTelemetry.FailuresTotal.Add(
                1,
                new KeyValuePair<string, object?>("job.type", trigger.JobType),
                new KeyValuePair<string, object?>("reason_kind", "event_binding"));
        }
    }

    private async Task<(Guid Id, bool NeedsSearch)?> ResolveAutomaticReleaseSearchTargetAsync(
        MagpieEvent magpieEvent,
        IContentHierarchyItemRepository contentHierarchyItemRepository,
        CancellationToken cancellationToken)
    {
        if (magpieEvent.Name is not (MagpieEvents.Names.MetadataRefreshed or
                MagpieEvents.Names.ContentFulfillmentChanged) ||
            !magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ContentHierarchyItemId, out var rawId) ||
            !Guid.TryParse(rawId, out var contentHierarchyItemId))
        {
            return null;
        }

        var item = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        if (item.ContentItem?.Fulfillment is not (FulfillmentStatus.Fulfilled or
                FulfillmentStatus.FulfilledExternally))
        {
            return (item.Id, true);
        }

        var leafIds = await contentHierarchyItemRepository.QueryIdsAsync(
            new ContentHierarchyItemCriteria { Ids = [contentHierarchyItemId], LeafOnly = true },
            new PagingParameter { PageNumber = 1, PageSize = 1 },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        if (leafIds.Count == 1)
        {
            return (item.Id, false);
        }

        var acquisitionCandidates = await contentHierarchyItemRepository.QueryIdsAsync(
            new ContentHierarchyItemCriteria
            {
                AncestorId = contentHierarchyItemId,
                LeafOnly = true,
                EffectiveMonitoring = true,
                Fulfillment = new()
                {
                    And = [new()
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = [FulfillmentStatus.Missing, FulfillmentStatus.Upgradable],
                    }],
                },
            },
            new PagingParameter { PageNumber = 1, PageSize = 1 },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        return (item.Id, acquisitionCandidates.Count > 0);
    }

    private async Task CancelAutomaticReleaseSearchesAsync(
        Guid contentHierarchyItemId,
        IJobManager jobManager,
        IJobExecutionRepository jobExecutionRepository,
        CancellationToken cancellationToken)
    {
        var executions = await jobExecutionRepository.QueryAllAsync(
            new JobExecutionQuery
            {
                JobType = MagpieJobs.Types.ReleaseSearchItem,
                Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running],
                ParameterValueReferences = [contentHierarchyItemId.ToString("D", CultureInfo.InvariantCulture)],
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var automaticSearches = executions
            .Where(execution => IsAutomaticUnforcedSearchFor(execution, contentHierarchyItemId))
            .ToList();

        foreach (var search in automaticSearches)
        {
            var executionId = search.Id;
            try
            {
                if (!await CancelAsync(search, jobManager, jobExecutionRepository, cancellationToken)
                    .ConfigureAwait(false))
                {
                    var current = await jobExecutionRepository.GetByIdAsync(executionId, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is not null && IsAutomaticUnforcedSearchFor(current, contentHierarchyItemId))
                    {
                        await CancelAsync(
                            current,
                            jobManager,
                            jobExecutionRepository,
                            cancellationToken).ConfigureAwait(false);
                    }
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
                    "The event dispatch service failed to cancel release search execution {ExecutionId}",
                    executionId);
            }
        }
    }

    private async Task<bool> CancelAsync(
        JobExecution search,
        IJobManager jobManager,
        IJobExecutionRepository jobExecutionRepository,
        CancellationToken cancellationToken) =>
        search.Status switch
        {
            JobExecutionStatus.Queued => await jobExecutionRepository
                .CancelAsync(search, cancellationToken)
                .ConfigureAwait(false) is not null,
            JobExecutionStatus.Running => await jobManager
                .CancelAsync(search.Id, cancellationToken)
                .ConfigureAwait(false),
            _ => true
        };

    private bool IsAutomaticUnforcedSearchFor(JobExecution execution, Guid contentHierarchyItemId) =>
        execution.JobType == MagpieJobs.Types.ReleaseSearchItem &&
        execution.ExecutionMode == JobExecutionMode.Queue &&
        (execution.EventTriggerId is not null ||
            execution.ScheduleTriggerId is not null ||
            execution.ParentExecutionId is not null) &&
        TargetsUnforcedSearch(execution.Parameters, contentHierarchyItemId);

    private bool IsUnforcedReleaseSearchFor(JobEnqueueRequest request, Guid contentHierarchyItemId)
    {
        return request.JobType == MagpieJobs.Types.ReleaseSearchItem &&
            TargetsUnforcedSearch(request.Parameters, contentHierarchyItemId);
    }

    private bool TargetsUnforcedSearch(
        IReadOnlyDictionary<string, string>? parameters,
        Guid contentHierarchyItemId) =>
        parameters is not null &&
        !MagpieJobs.Parameters.IsForced(parameters) &&
        parameters.TryGetValue(MagpieJobs.Parameters.ContentHierarchyItemId, out var rawId) &&
        Guid.TryParse(rawId, out var targetId) &&
        targetId == contentHierarchyItemId;

    private async Task<List<JobEnqueueRequest>> CollectFailedDownloadRetryRequestsAsync(
        MagpieEvent magpieEvent,
        Guid? correlationId,
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
            var scopes = await downloadScopeRepository.QueryAllAsync(
                new DownloadScopeQuery { DownloadId = downloadId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var hierarchyItemIds = scopes.Select(scope => scope.ContentHierarchyItemId).ToHashSet();

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

            const int contentBatchSize = 500;
            var contentItems = new List<ContentItem>();
            foreach (var batch in leafHierarchyItemIds.Distinct().Chunk(contentBatchSize))
            {
                var contentResults = await contentRepository.QueryAsync(
                    new() { ContentHierarchyItemIds = batch },
                    new() { PageNumber = 1, PageSize = batch.Length },
                    null,
                    cancellationToken).ConfigureAwait(false);
                contentItems.AddRange(contentResults.Items);
            }

            var retryCooldown = await ResolveFailedDownloadRetryCooldownAsync(
                scheduleTriggerRepository,
                cancellationToken)
                .ConfigureAwait(false);
            var notBeforeAt = _time.GetUtcNow().UtcDateTime.Add(retryCooldown);
            return
            [
                .. contentItems
                    .Where(contentItem =>
                        contentItem.Fulfillment is FulfillmentStatus.Missing or FulfillmentStatus.Upgradable)
                    .Select(contentItem => new JobEnqueueRequest
                    {
                        JobType = MagpieJobs.Types.ReleaseAutoGrabItem,
                        Parameters = new Dictionary<string, string>
                        {
                            [MagpieJobs.Parameters.ContentItemId] = contentItem.Id.ToString()
                        },
                        Mode = JobExecutionMode.Queue,
                        TriggerContext = magpieEvent.ToTriggerContext(),
                        UserId = systemUserId,
                        NotBeforeAt = notBeforeAt,
                        CorrelationId = correlationId,
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
                    JobType = MagpieJobs.Types.ReleaseAutoGrabScan,
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
                "Retry cooldown {Raw} for the auto-grab scan is not a non-negative integer; using " +
                "default {Default}",
                raw,
                MagpieJobs.ParameterValues.DefaultRetryCooldownHours);
        }

        return TimeSpan.FromHours(MagpieJobs.ParameterValues.DefaultRetryCooldownHours);
    }

    private async Task<Guid?> ResolveAuthoritativeMetadataRefreshTargetAsync(
        MagpieEvent magpieEvent,
        IEnumerable<EventTrigger> triggers,
        IJobManager jobManager,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreatedHierarchyItemId(magpieEvent, out var contentHierarchyItemId) ||
            magpieEvent.JobExecutionId is not { } parentExecutionId ||
            !triggers.Any(trigger =>
                trigger.JobType == MagpieJobs.Types.ContentMetadataRefreshItem &&
                TargetsContentHierarchyItem(trigger, contentHierarchyItemId)))
        {
            return null;
        }

        try
        {
            var parent = await jobManager.GetExecutionAsync(parentExecutionId, cancellationToken)
                .ConfigureAwait(false);
            if (parent is null ||
                parent.JobType != MagpieJobs.Types.ContentMetadataRefreshItem ||
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
                        JobType = MagpieJobs.Types.ContentMetadataRefreshItem,
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
        Guid? correlationId,
        Guid? systemUserId,
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

                var parameters = ResolveEventParameters(trigger.Parameters, magpieEvent.Context);

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
                    CorrelationId = correlationId,
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

    private string ResolveEventParameter(
        string expression,
        IReadOnlyDictionary<string, string> eventContext)
    {
        return TransformEventParameter(
            expression,
            key => eventContext.TryGetValue(key, out var value)
                ? value
                : throw new MagpieException($"Event context key {key} is missing."));
    }

    private Dictionary<string, string> ResolveEventParameters(
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

    private void ValidateResolvedParameterTypes(
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

    private string TransformEventParameter(
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
        Guid? correlationId,
        IEnumerable<NotificationRule> rules,
        IEnumerable<INotificationProvider> notificationProviders)
    {
        var requests = new List<JobEnqueueRequest>();
        if (IsNotificationDeliveryFailure(magpieEvent))
        {
            return requests;
        }

        var notificationProviderTypes = new HashSet<string>(notificationProviders
            .Select(provider => provider.ProviderType)
            .Where(type => !string.IsNullOrWhiteSpace(type)));
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
                    "The event dispatch service skipped notification rule {RuleName} ({RuleId}) because " +
                    "its filter failed for event {EventName}",
                    rule.Name,
                    rule.Id,
                    magpieEvent.Name);
                continue;
            }

            if (!matched)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.ProviderType) || !notificationProviderTypes.Contains(rule.ProviderType))
            {
                logger.LogWarning(
                    "The event dispatch service skipped notification rule {RuleName} ({RuleId}) with " +
                    "unknown provider type {ProviderType}",
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
                JobType = MagpieJobs.Types.SendNotification,
                Parameters = parameters,
                TriggerContext = magpieEvent.ToTriggerContext(),
                Mode = JobExecutionMode.Queue,
                CorrelationId = correlationId,
            });
        }

        return requests;
    }
}
