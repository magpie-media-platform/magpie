using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Diagnostics;

internal sealed class MissingDefaultJobRepair(
    IScheduleTriggerRepository scheduleTriggers,
    IEventTriggerRepository eventTriggers,
    TimeProvider timeProvider,
    ILogger<MissingDefaultJobRepair> logger) : IDiagnosticRepair
{
    public const string Id = "core.config.reseed-default-job";

    public string RepairId => Id;

    internal static bool IsUnboundDefaultEventTrigger(EventTrigger trigger, EventTrigger entry) =>
        entry.Parameters.Count > 0 &&
        trigger.Parameters.Count == 0 &&
        trigger.CreatedByUserId is null &&
        trigger.ModifiedByUserId is null &&
        string.Equals(trigger.JobType, entry.JobType, StringComparison.Ordinal) &&
        string.Equals(
            trigger.EventFilter.EventName?.Value,
            entry.EventFilter.EventName?.Value,
            StringComparison.Ordinal);

    public async Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default)
    {
        var affected = finding.AffectedEntity;
        if (affected is not null &&
            affected.StartsWith(JobDiagnosticAffectedEntityPrefixes.EventTriggerJob, StringComparison.Ordinal))
        {
            return await ReseedEventTriggerAsync(
                affected[JobDiagnosticAffectedEntityPrefixes.EventTriggerJob.Length..],
                cancellationToken)
                .ConfigureAwait(false);
        }

        if (affected is not null &&
            affected.StartsWith(JobDiagnosticAffectedEntityPrefixes.EventTrigger, StringComparison.Ordinal))
        {
            return await BindEventTriggerAsync(
                affected[JobDiagnosticAffectedEntityPrefixes.EventTrigger.Length..],
                cancellationToken)
                .ConfigureAwait(false);
        }

        if (affected is null || !affected.StartsWith(JobDiagnosticAffectedEntityPrefixes.Job, StringComparison.Ordinal))
        {
            return DiagnosticRepairResult.Failed("The finding does not identify a default job to re-seed.");
        }

        var jobType = affected[JobDiagnosticAffectedEntityPrefixes.Job.Length..];
        var entry = DefaultJobTriggerCatalog.ScheduleTriggers
            .FirstOrDefault(e => string.Equals(e.JobType, jobType, StringComparison.Ordinal));
        if (entry is null)
        {
            return DiagnosticRepairResult.Failed($"{jobType} is not a known default job.");
        }

        var existingSchedules = (await scheduleTriggers
            .QueryAsync(
                new ScheduleTriggerQuery { JobType = jobType },
                new() { PageNumber = 1, PageSize = 1 },
                sort: null,
                cancellationToken)
            .ConfigureAwait(false)).Items;
        if (existingSchedules.Any())
        {
            return DiagnosticRepairResult.Succeeded($"A schedule for {jobType} already exists; nothing to re-seed.");
        }

        var trigger = new ScheduleTrigger
        {
            JobType = entry.JobType,
            Scope = entry.Scope,
            Name = entry.Name,
            Schedule = entry.Schedule,
            ExecutionMode = entry.ExecutionMode,
            Enabled = true,
            Parameters = [],
            LastRunAt = null,
            NextRunAt = CronSchedule.TryParse(entry.Schedule, out var cronSchedule, out _)
                ? cronSchedule.NextOccurrence(timeProvider.GetUtcNow().UtcDateTime)
                : null,
        };

        await scheduleTriggers.CreateAsync(trigger, null, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The diagnostic repair re-seeded schedule {TriggerId} for {JobType} with schedule {Schedule}",
            trigger.Id,
            entry.JobType,
            entry.Schedule);

        return DiagnosticRepairResult.Succeeded($"Re-seeded the {entry.JobType} schedule.");
    }

    private async Task<DiagnosticRepairResult> BindEventTriggerAsync(
        string rawTriggerId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(rawTriggerId, out var triggerId))
        {
            return DiagnosticRepairResult.Failed("The finding does not identify an event trigger.");
        }

        var trigger = await eventTriggers.GetByIdAsync(triggerId, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            return DiagnosticRepairResult.Failed($"Event trigger {triggerId} no longer exists.");
        }

        if (trigger.ModifiedByUserId is not null)
        {
            return DiagnosticRepairResult.Failed($"Event trigger {trigger.Name} was modified by a user.");
        }

        var entry = DefaultJobTriggerCatalog.EventTriggers
            .FirstOrDefault(candidate => IsUnboundDefaultEventTrigger(trigger, candidate));
        if (entry is null)
        {
            return DiagnosticRepairResult.Succeeded(
                $"Event trigger {trigger.Name} has parameters or is not a built-in trigger; nothing to bind.");
        }

        await eventTriggers.PatchAsync(
            new EventTriggerPatch
            {
                Id = trigger.Id,
                Parameters = new() { Add = new(entry.Parameters, StringComparer.Ordinal) }
            },
            null,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The diagnostic repair bound event trigger {TriggerId} for {JobType} to the built-in parameters",
            trigger.Id,
            trigger.JobType);

        return DiagnosticRepairResult.Succeeded(
            $"Bound the {trigger.JobType} event trigger {trigger.Name} to {string.Join(", ", entry.Parameters.Keys)}.");
    }

    private async Task<DiagnosticRepairResult> ReseedEventTriggerAsync(
        string jobType,
        CancellationToken cancellationToken)
    {
        var entry = DefaultJobTriggerCatalog.EventTriggers
            .FirstOrDefault(e => string.Equals(e.JobType, jobType, StringComparison.Ordinal));
        if (entry is null)
        {
            return DiagnosticRepairResult.Failed($"{jobType} is not a known default event trigger.");
        }

        var existingTriggers = (await eventTriggers.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(trigger => string.Equals(trigger.JobType, jobType, StringComparison.Ordinal));
        if (existingTriggers.Any())
        {
            return DiagnosticRepairResult.Succeeded(
                $"An event trigger for {jobType} already exists; nothing to re-seed.");
        }

        var trigger = new EventTrigger
        {
            JobType = entry.JobType,
            Scope = entry.Scope,
            Name = entry.Name,
            EventFilter = entry.EventFilter,
            Enabled = true,
            DelaySeconds = entry.DelaySeconds,
            Parameters = new(entry.Parameters, StringComparer.Ordinal),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };

        await eventTriggers.CreateAsync(trigger, null, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The diagnostic repair re-seeded event trigger {TriggerId} for {JobType}",
            trigger.Id,
            entry.JobType);

        return DiagnosticRepairResult.Succeeded($"Re-seeded the {entry.JobType} event trigger.");
    }
}
