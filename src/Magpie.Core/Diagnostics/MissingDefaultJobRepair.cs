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

    public async Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default)
    {
        var affected = finding.AffectedEntity;
        if (affected is not null &&
            affected.StartsWith(
                JobDiagnosticAffectedEntityPrefixes.EventTriggerJob, StringComparison.Ordinal))
        {
            return await ReseedEventTriggerAsync(
                affected[JobDiagnosticAffectedEntityPrefixes.EventTriggerJob.Length..],
                cancellationToken)
                .ConfigureAwait(false);
        }

        if (affected is null ||
            !affected.StartsWith(JobDiagnosticAffectedEntityPrefixes.Job, StringComparison.Ordinal))
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
            Parameters = [],
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
