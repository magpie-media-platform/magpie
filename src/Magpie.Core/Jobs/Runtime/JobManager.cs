using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

using JobScopeKind = Magpie.Models.Enums.JobScope;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class JobManager(
    IJobRuntime runtime,
    IJobRegistry jobRegistry,
    IJobExecutionRepository executionRepository,
    IScheduleTriggerRepository scheduleTriggerRepository,
    IEventTriggerRepository eventTriggerRepository,
    IJobConcurrencyScheduler scheduler,
    IJobExecutionScopeFactory jobExecutionScopes,
    TimeProvider timeProvider,
    ILogger<JobManager> logger) : IJobManager
{
    private readonly IReadOnlyList<JobSpec> _specs = jobRegistry.ListJobSpecs();

    private const string GlobalPartition = "global";

    private const int ExecutionTreePageSize = 500;

    private const int MaxExecutionTreeNodes = 5000;

    private static JobSpec? GetRegisteredSpec(IReadOnlyList<JobSpec> specs, string jobType)
    {
        if (string.IsNullOrWhiteSpace(jobType))
        {
            return null;
        }

        return specs.FirstOrDefault(spec => string.Equals(spec.JobType, jobType, StringComparison.Ordinal));
    }

    private static void AuthorizeManualRun(JobSpec? spec, Role callerRole)
    {
        if (spec is null || !spec.OnDemand || !spec.SupportedTriggerModes.Contains(JobTriggerMode.Manual))
        {
            throw new MagpieException("This job type cannot be run on demand.");
        }

        AuthorizeRequiredRole(spec, callerRole);
    }

    private static Dictionary<string, string?> BuildLockPartitionParameters(JobExecution execution, JobSpec? spec)
    {
        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var parameter in execution.Parameters)
        {
            parameters[parameter.Key] = parameter.Value;
        }

        if (spec is null || spec.LockPartitionEventContextKeys.Count == 0)
        {
            return parameters;
        }

        foreach (var (field, eventContextKey) in spec.LockPartitionEventContextKeys)
        {
            if (parameters.TryGetValue(field, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            if (execution.TriggerContext.TryGetValue(eventContextKey.ToEventContextKey(), out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                parameters[field] = value;
            }
        }

        return parameters;
    }

    private static void InjectCurrentTrace(IDictionary<string, string> triggerContext)
    {
        if (Activity.Current is not { } activity ||
            DistributedContextPropagator.Current.Fields.Any(triggerContext.ContainsKey))
        {
            return;
        }

        DistributedContextPropagator.Current.Inject(
            activity,
            triggerContext,
            static (carrier, key, value) => ((IDictionary<string, string>)carrier!)[key] = value);
    }

    private static string? ResolveLockPartition(
        ConcurrencyScope scope,
        string jobType,
        IReadOnlyDictionary<string, string?> parameters,
        IReadOnlyList<string> lockPartitionFields)
    {
        return scope switch
        {
            ConcurrencyScope.Parallel => null,
            ConcurrencyScope.PerType => $"type:{jobType}",
            ConcurrencyScope.Global => GlobalPartition,
            ConcurrencyScope.PerTypeAndTarget => BuildTargetKey(parameters, lockPartitionFields) is { } targetKey
                ? $"type:{jobType}|{targetKey}"
                : UniquePartition(),
            ConcurrencyScope.PerTarget => BuildTargetKey(parameters, lockPartitionFields) ?? UniquePartition(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown concurrency scope.")
        };
    }

    private static string? BuildTargetKey(
        IReadOnlyDictionary<string, string?> parameters,
        IReadOnlyList<string> lockPartitionFields)
    {
        if (lockPartitionFields.Count == 0)
        {
            return null;
        }

        var parts = new List<string>(lockPartitionFields.Count);
        foreach (var field in lockPartitionFields.OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!parameters.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            parts.Add($"{field}={value}");
        }

        return string.Join("&", parts);
    }

    private static string UniquePartition() => $"unique:{Guid.NewGuid():N}";

    private static void AuthorizeRequiredRole(JobSpec spec, Role callerRole)
    {
        if (callerRole < GetRequiredRole(spec.Scope))
        {
            throw new MagpieException("You do not have permission to run this job type.");
        }
    }

    private static void ValidateParameters(
        JobSpec spec,
        IReadOnlyDictionary<string, string>? parameters,
        bool requireRequired = true)
    {
        foreach (var parameter in spec.Parameters)
        {
            if (parameters is null ||
                !parameters.TryGetValue(parameter.Name, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                if (requireRequired && parameter.Required)
                {
                    throw new MagpieException($"Required parameter {parameter.Name} is missing.");
                }

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

    private static void ValidateEventTriggerParameters(
        JobSpec spec,
        IReadOnlyDictionary<string, string> parameters)
    {
        var resolved = new Dictionary<string, string>(parameters.Count);
        foreach (var (name, expression) in parameters)
        {
            var type = spec.Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Type ?? DataType.String;
            resolved[name] = TransformEventParameter(expression, EventBindingValidationSample(type));
        }

        ValidateParameters(spec, resolved, requireRequired: false);
    }

    private static void ValidateEventFilter(EventFilter? filter)
    {
        if (filter is null)
        {
            return;
        }

        ValidateRegexCriterion(filter.EventName);
        foreach (var criterion in filter.EventContext?.Items.Values.AsEnumerable() ?? [])
        {
            ValidateRegexCriterion(criterion);
        }
    }

    private static void ValidateRegexCriterion(PropertyCriterion<string>? criterion)
    {
        if (criterion is null || criterion.Operator != CriteriaValueOperator.Regex || criterion.Value is null)
        {
            return;
        }

        try
        {
            _ = new Regex(criterion.Value);
        }
        catch (ArgumentException)
        {
            throw new MagpieException("Event filter regular expression is malformed.");
        }
    }

    private static string EventBindingValidationSample(DataType type) => type switch
    {
        DataType.Number => "0",
        DataType.Boolean => "true",
        DataType.Date => "2000-01-01",
        _ => string.Empty,
    };

    private static string TransformEventParameter(string expression, string replacement)
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

            if (!expression.AsSpan(index).StartsWith("${", StringComparison.Ordinal))
            {
                result.Append(expression[index]);
                index++;
                continue;
            }

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

            result.Append(replacement);
            index = end + 1;
        }

        return result.ToString();
    }

    private static Role GetRequiredRole(JobScopeKind scope)
    {
        return scope switch
        {
            JobScopeKind.User => Role.Requester,
            JobScopeKind.Content => Role.Manager,
            JobScopeKind.Administrative or JobScopeKind.System => Role.Admin,
            _ => Role.Admin
        };
    }

    private static void AuthorizeTriggerMutation(
        JobSpec? spec,
        JobScopeKind scope,
        Guid? ownerUserId,
        Guid callerUserId,
        bool callerIsAdmin)
    {
        if (spec?.Scope == JobScopeKind.System)
        {
            throw new MagpieException("Only Magpie can create, change, or delete built-in triggers.");
        }

        if (!callerIsAdmin && (scope != JobScopeKind.User || ownerUserId != callerUserId))
        {
            throw new MagpieException("You can only modify your own triggers.");
        }
    }

    private static void AuthorizeTriggerCreate(JobSpec? spec, JobTriggerMode mode, Role ownerRole)
    {
        if (spec is null || !spec.SupportedTriggerModes.Contains(mode))
        {
            throw new MagpieException("This job type does not support this trigger mode.");
        }

        if (spec.Scope == JobScopeKind.System)
        {
            throw new MagpieException("Only Magpie can create, change, or delete built-in triggers.");
        }

        AuthorizeRequiredRole(spec, ownerRole);
    }

    private static JobExecutionNode BuildExecutionNode(
        JobExecution execution,
        IReadOnlyDictionary<Guid, List<JobExecution>> byParent,
        HashSet<Guid> visited,
        DateTime utcNow)
    {
        visited.Add(execution.Id);

        var children = byParent.TryGetValue(execution.Id, out var rows)
            ? rows
                .Where(child => !visited.Contains(child.Id))
                .Select(child => BuildExecutionNode(child, byParent, visited, utcNow))
                .ToList()
            : [];

        return new JobExecutionNode
        {
            Id = execution.Id,
            ParentExecutionId = execution.ParentExecutionId,
            CorrelationId = execution.CorrelationId,
            JobType = execution.JobType,
            Status = execution.Status is JobExecutionStatus.Queued &&
                execution.NotBeforeAt is { } notBeforeAt &&
                notBeforeAt > utcNow
                    ? "Pending"
                    : execution.Status.ToString(),
            CreatedAt = execution.CreatedAt,
            StartedAt = execution.StartedAt,
            CompletedAt = execution.CompletedAt,
            Children = children
        };
    }

    public async Task<Guid> EnqueueAsync(
        JobEnqueueRequest request,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken)
    {
        var spec = GetSpec(request.JobType);
        AuthorizeManualRun(spec, callerRole);
        ValidateParameters(spec!, request.Parameters);

        var authorized = request with { UserId = callerUserId, Mode = JobExecutionMode.Queue };
        return await ExecuteAsync(authorized, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobExecution> ExecuteImmediateAsync(
        JobEnqueueRequest request,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken)
    {
        var spec = GetSpec(request.JobType);
        AuthorizeManualRun(spec, callerRole);
        ValidateParameters(spec!, request.Parameters);

        if (!spec!.SupportsImmediate)
        {
            throw new MagpieException("This job type cannot be run immediately.");
        }

        var authorized = request with { UserId = callerUserId, Mode = JobExecutionMode.Immediate };
        var executionId = await ExecuteAsync(authorized, cancellationToken).ConfigureAwait(false);

        return await executionRepository.GetByIdAsync(executionId, cancellationToken).ConfigureAwait(false)
            ?? throw new MagpieException("The job execution could not be found after running.");
    }

    public async Task<Guid> ExecuteAsync(JobEnqueueRequest request, CancellationToken cancellationToken)
    {
        var effectiveCorrelationId = request.CorrelationId ?? Guid.NewGuid();

        logger.LogDebug(
            "The job manager will run job {JobType} for user {UserId} with mode {Mode} (correlation {CorrelationId})",
            request.JobType,
            request.UserId,
            request.Mode,
            effectiveCorrelationId);

        var spec = GetSpec(request.JobType);

        var effectiveParameters = request.Parameters is not null
            ? new Dictionary<string, string>(request.Parameters)
            : [];

        string? dedupKey = null;
        if (effectiveParameters.Remove(JobExecutionDedup.ParameterKey, out var rawDedupKey) &&
            !string.IsNullOrWhiteSpace(rawDedupKey))
        {
            dedupKey = rawDedupKey;
        }

        var execution = new JobExecution
        {
            ExtensionId = request.ExtensionId,
            EventTriggerId = request.EventTriggerId,
            JobType = request.JobType,
            Scope = spec?.Scope ?? JobScopeKind.System,
            Parameters = effectiveParameters,
            TriggerContext = request.TriggerContext is not null
                ? new Dictionary<string, string>(request.TriggerContext)
                : [],
            ExecutionMode = request.Mode,
            Status = JobExecutionStatus.Queued,
            CorrelationId = effectiveCorrelationId,
            ParentExecutionId = request.IsSuccessor ? null : jobExecutionScopes.CurrentExecutionId,
            DedupKey = dedupKey,
            NotBeforeAt = request.NotBeforeAt,
        };

        InjectCurrentTrace(execution.TriggerContext);

        execution.LockPartition = ResolveLockPartition(
            spec?.ConcurrencyScope ?? ConcurrencyScope.PerType,
            execution.JobType,
            BuildLockPartitionParameters(execution, spec),
            spec?.LockPartitionFields ?? []);

        var created = await executionRepository.CreateAsync(
            execution,
            request.UserId,
            cancellationToken)
            .ConfigureAwait(false);

        if (request.Mode is JobExecutionMode.Immediate && created.Status is JobExecutionStatus.Queued)
        {
            using var reservation = await scheduler.AcquireImmediateSlotAsync(cancellationToken).ConfigureAwait(false);

            await runtime.ExecuteAsync(created.Id, default).ConfigureAwait(false);
        }

        return created.Id;
    }

    public string? ResolveLockPartition(string jobType, IReadOnlyDictionary<string, string?> parameters)
    {
        var spec = GetSpec(jobType);
        return ResolveLockPartition(
            spec?.ConcurrencyScope ?? ConcurrencyScope.PerType,
            jobType,
            parameters,
            spec?.LockPartitionFields ?? []);
    }

    public async Task<PagedList<JobExecution>> QueryExecutionsAsync(
        JobExecutionQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        return await executionRepository.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JobExecution?> GetExecutionAsync(Guid executionId, CancellationToken cancellationToken)
    {
        return await executionRepository.GetByIdAsync(executionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JobExecutionTree?> GetExecutionTreeAsync(
        Guid executionId,
        bool includeSiblings,
        int maxNodes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        var nodeLimit = Math.Min(maxNodes, MaxExecutionTreeNodes);
        var lineage = await executionRepository
            .GetLineageAsync(executionId, ExecutionTreePageSize, nodeLimit, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            return null;
        }

        var byId = lineage.ToDictionary(e => e.Id);
        if (!byId.TryGetValue(executionId, out var target))
        {
            return new JobExecutionTree { Root = null, TotalCount = 0 };
        }

        var root = target;
        if (includeSiblings)
        {
            var ancestors = new HashSet<Guid> { root.Id };
            while (root.ParentExecutionId is { } parentId &&
                byId.TryGetValue(parentId, out var parent) &&
                ancestors.Add(parent.Id))
            {
                root = parent;
            }
        }

        var byParent = lineage
            .Where(e => e.ParentExecutionId is not null)
            .GroupBy(e => e.ParentExecutionId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList());

        var visited = new HashSet<Guid>();
        var rootNode = BuildExecutionNode(root, byParent, visited, timeProvider.GetUtcNow().UtcDateTime);
        return new JobExecutionTree
        {
            Root = rootNode,
            TotalCount = visited.Count,
            Truncated = lineage.Count >= nodeLimit
        };
    }

    public async Task<bool> RequestCancellationAsync(Guid executionId, CancellationToken cancellationToken)
    {
        logger.LogDebug("The job manager will ask the runtime to cancel execution {ExecutionId}", executionId);

        return await runtime.TryCancelAsync(executionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public IReadOnlyList<JobSpec> GetSpecs(JobTriggerMode? userTriggerMode)
    {
        var specs = _specs;
        if (userTriggerMode is not { } mode)
        {
            return specs;
        }

        return mode is JobTriggerMode.Manual
            ? specs.Where(s => s.OnDemand && s.SupportedTriggerModes.Contains(JobTriggerMode.Manual)).ToList()
            : [.. specs.Where(s => s.SupportedTriggerModes.Contains(mode) && s.RequesterTriggerModes.Contains(mode))];
    }

    public async Task<IReadOnlyList<ScheduleTrigger>> ListScheduleTriggersAsync(
        bool mineOnly,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        var triggers = await scheduleTriggerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return mineOnly ? [.. triggers.Where(trigger => trigger.CreatedByUserId == callerUserId)] : [.. triggers];
    }

    public Task<ScheduleTrigger?> GetScheduleTriggerAsync(Guid triggerId, CancellationToken cancellationToken)
    {
        return scheduleTriggerRepository.GetByIdAsync(triggerId, cancellationToken);
    }

    public async Task<ScheduleTrigger> CreateScheduleTriggerAsync(
        ScheduleTriggerCreate create,
        Guid ownerUserId,
        Role ownerRole,
        CancellationToken cancellationToken)
    {
        AuthorizeTriggerCreate(GetSpec(create.JobType), JobTriggerMode.Schedule, ownerRole);
        if (!CronSchedule.TryParse(create.Schedule, out var schedule, out var error))
        {
            throw new ArgumentException(error, nameof(create));
        }

        return await scheduleTriggerRepository.CreateAsync(
            new ScheduleTrigger
            {
                ExtensionId = create.ExtensionId,
                JobType = create.JobType,
                Scope = create.Scope,
                Name = create.Name,
                Schedule = create.Schedule,
                ExecutionMode = create.ExecutionMode,
                Enabled = create.Enabled,
                Parameters = create.Parameters,
                CreatedByUserId = ownerUserId,
                NextRunAt = create.Enabled ? schedule.NextOccurrence(timeProvider.GetUtcNow().UtcDateTime) : null
            },
            ownerUserId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PatchScheduleTriggerAsync(
        Guid triggerId,
        ScheduleTriggerPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken)
    {
        var trigger = await scheduleTriggerRepository.GetByIdAsync(triggerId, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return false;
        }

        AuthorizeTriggerMutation(GetSpec(trigger.JobType), trigger.Scope, trigger.CreatedByUserId, callerUserId,
            callerIsAdmin);
        if (patch.Schedule is not null && !CronSchedule.TryParse(patch.Schedule.Value, out _, out var error))
        {
            throw new ArgumentException(error, nameof(patch));
        }

        ScalarPatch<DateTime?>? nextRunAt = null;
        if (patch.Schedule is not null || patch.Enabled is not null)
        {
            var enabled = patch.Enabled?.Value ?? trigger.Enabled;
            var cron = patch.Schedule?.Value ?? trigger.Schedule;
            nextRunAt = new()
            {
                Value = enabled && CronSchedule.TryParse(cron, out var schedule, out _)
                    ? schedule.NextOccurrence(timeProvider.GetUtcNow().UtcDateTime)
                    : null
            };
        }

        return await scheduleTriggerRepository
            .PatchAsync(patch with { Id = triggerId, NextRunAt = nextRunAt }, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteScheduleTriggerAsync(
        Guid triggerId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken)
    {
        var trigger = await scheduleTriggerRepository.GetByIdAsync(triggerId, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return false;
        }

        AuthorizeTriggerMutation(GetSpec(trigger.JobType), trigger.Scope, trigger.CreatedByUserId, callerUserId,
            callerIsAdmin);
        return await scheduleTriggerRepository.DeleteAsync(triggerId, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EventTrigger>> ListEventTriggersAsync(
        Guid? extensionId,
        bool? enabled,
        bool mineOnly,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        var triggers = await eventTriggerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. triggers
            .Where(trigger => extensionId is null || trigger.ExtensionId == extensionId)
            .Where(trigger => enabled is null || trigger.Enabled == enabled)
            .Where(trigger => !mineOnly || trigger.CreatedByUserId == callerUserId)];
    }

    public Task<EventTrigger?> GetEventTriggerAsync(Guid triggerId, CancellationToken cancellationToken)
    {
        return eventTriggerRepository.GetByIdAsync(triggerId, cancellationToken);
    }

    public async Task<EventTrigger> CreateEventTriggerAsync(
        EventTriggerCreate create,
        Guid ownerUserId,
        Role ownerRole,
        CancellationToken cancellationToken)
    {
        var spec = GetSpec(create.JobType);
        AuthorizeTriggerCreate(spec, JobTriggerMode.Event, ownerRole);
        ValidateEventTriggerParameters(spec!, create.Parameters);
        ValidateEventFilter(create.EventFilter);
        return await eventTriggerRepository.CreateAsync(
            new EventTrigger
            {
                ExtensionId = create.ExtensionId,
                JobType = create.JobType,
                Scope = create.Scope,
                Name = create.Name,
                EventFilter = create.EventFilter,
                DelaySeconds = create.DelaySeconds,
                Enabled = create.Enabled,
                Parameters = create.Parameters,
                CreatedByUserId = ownerUserId
            },
            ownerUserId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PatchEventTriggerAsync(
        Guid triggerId,
        EventTriggerPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken)
    {
        var trigger = await eventTriggerRepository.GetByIdAsync(triggerId, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return false;
        }

        var spec = GetSpec(trigger.JobType);

        AuthorizeTriggerMutation(spec, trigger.Scope, trigger.CreatedByUserId, callerUserId, callerIsAdmin);

        if (spec is not null)
        {
            var parameters = new Dictionary<string, string>(trigger.Parameters);
            foreach (var key in patch.Parameters?.Remove ?? [])
            {
                parameters.Remove(key);
            }

            foreach (var (key, value) in patch.Parameters?.Add ?? [])
            {
                parameters[key] = value;
            }

            foreach (var (key, value) in patch.Parameters?.Update ?? [])
            {
                if (parameters.ContainsKey(key))
                {
                    parameters[key] = value;
                }
            }

            ValidateEventTriggerParameters(spec, parameters);
        }

        ValidateEventFilter(patch.EventFilter?.Value);

        return await eventTriggerRepository.PatchAsync(
            patch with { Id = triggerId }, callerUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteEventTriggerAsync(
        Guid triggerId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken)
    {
        var trigger = await eventTriggerRepository.GetByIdAsync(triggerId, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return false;
        }

        AuthorizeTriggerMutation(GetSpec(trigger.JobType), trigger.Scope, trigger.CreatedByUserId, callerUserId,
            callerIsAdmin);
        return await eventTriggerRepository.DeleteAsync(triggerId, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private JobSpec? GetSpec(string jobType) => GetRegisteredSpec(_specs, jobType);
}
