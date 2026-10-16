using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Diagnostics;

internal sealed class MissingDefaultJobRepair(
    IScheduleTriggerRepository scheduleTriggers,
    IEventTriggerRepository eventTriggers,
    ILogger<MissingDefaultJobRepair> logger) : IDiagnosticRepair
{
    public const string Id = "core.config.reseed-default-job";

    public string RepairId => Id;

    public async Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default)
    {
        var affected = finding.AffectedEntity ?? string.Empty;
        string jobType;
        var created = 0;
        if (affected.StartsWith(JobDiagnosticAffectedEntityPrefixes.EventTriggerJob, StringComparison.Ordinal))
        {
            jobType = affected[JobDiagnosticAffectedEntityPrefixes.EventTriggerJob.Length..];
            var stored = (await eventTriggers.ListAllAsync(cancellationToken).ConfigureAwait(false))
                .Select(trigger => (trigger.JobType, trigger.Name))
                .ToHashSet();
            foreach (var entry in DefaultJobTriggerCatalog.EventTriggers.Where(entry =>
                entry.JobType == jobType && !stored.Contains((entry.JobType, entry.Name))))
            {
                await eventTriggers.CreateAsync(entry, null, cancellationToken).ConfigureAwait(false);
                created++;
            }
        }
        else if (affected.StartsWith(JobDiagnosticAffectedEntityPrefixes.Job, StringComparison.Ordinal))
        {
            jobType = affected[JobDiagnosticAffectedEntityPrefixes.Job.Length..];
            var stored = (await scheduleTriggers.ListAllAsync(cancellationToken).ConfigureAwait(false))
                .Select(trigger => (trigger.JobType, trigger.Name))
                .ToHashSet();
            foreach (var entry in DefaultJobTriggerCatalog.ScheduleTriggers.Where(entry =>
                entry.JobType == jobType && !stored.Contains((entry.JobType, entry.Name))))
            {
                await scheduleTriggers.CreateAsync(entry, null, cancellationToken).ConfigureAwait(false);
                created++;
            }
        }
        else
        {
            return DiagnosticRepairResult.Failed("The finding does not identify a built-in job.");
        }

        logger.LogInformation(
            "The diagnostic repair recreated {TriggerCount} built-in trigger(s) for {JobType}",
            created,
            jobType);

        return DiagnosticRepairResult.Succeeded($"Recreated {created} built-in trigger(s) for {jobType}.");
    }
}
