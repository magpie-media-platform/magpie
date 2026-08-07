using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// Extension methods for job executions.
/// </summary>
public static class JobExecutionExtensions
{
    /// <summary>
    /// Builds the lock partition parameters for the execution.
    /// </summary>
    /// <param name="execution">The execution to partition</param>
    /// <param name="spec">The spec of the execution's job</param>
    public static Dictionary<string, string?> GetLockPartitionFor(this JobExecution execution, JobSpec? spec)
    {
        var parameters = execution.Parameters
            .ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value, StringComparer.Ordinal);

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

    /// <summary>
    /// Creates a queued retry copy of the execution.
    /// </summary>
    /// <param name="source">The execution to copy</param>
    /// <param name="originalId">The id of the original execution</param>
    /// <param name="attempt">The number of the retry attempt</param>
    /// <param name="notBeforeAt">The earliest UTC time the retry can run</param>
    public static JobExecution ToRetry(this JobExecution source, Guid originalId, int attempt, DateTime notBeforeAt)
    {
        return new JobExecution
        {
            JobType = source.JobType,
            ExtensionId = source.ExtensionId,
            ScheduleTriggerId = source.ScheduleTriggerId,
            EventTriggerId = source.EventTriggerId,
            Scope = source.Scope,
            Parameters = new(source.Parameters),
            TriggerContext = new(source.TriggerContext),
            Status = JobExecutionStatus.Queued,
            Attempt = attempt,
            OriginalExecutionId = originalId,
            ParentExecutionId = source.ParentExecutionId,
            CorrelationId = source.CorrelationId,
            LockPartition = source.LockPartition,
            NotBeforeAt = notBeforeAt,
        };
    }

    /// <summary>
    /// Creates a queued requeue copy of the execution.
    /// </summary>
    /// <param name="source">The execution to copy</param>
    /// <param name="originalId">The id of the original execution</param>
    public static JobExecution ToRequeue(this JobExecution source, Guid originalId)
    {
        return new JobExecution
        {
            JobType = source.JobType,
            ExtensionId = source.ExtensionId,
            ScheduleTriggerId = source.ScheduleTriggerId,
            EventTriggerId = source.EventTriggerId,
            Scope = source.Scope,
            Parameters = new(source.Parameters),
            TriggerContext = new(source.TriggerContext),
            Status = JobExecutionStatus.Queued,
            OriginalExecutionId = originalId,
            ParentExecutionId = source.ParentExecutionId,
            CorrelationId = source.CorrelationId,
            LockPartition = source.LockPartition,
        };
    }
}
