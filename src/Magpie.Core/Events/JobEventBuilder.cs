using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class JobEventBuilder
{
    public static MagpieEvent BuildJobCancellationRequestedEvent(Guid executionId, string ownerNodeId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExecutionId] = executionId.ToString("D", CultureInfo.InvariantCulture),
            ["execution.ownerNodeId"] = ownerNodeId ?? string.Empty,
        };

        return new(Guid.Empty, "job.cancellation.requested", context, default, default, null);
    }

    public static MagpieEvent BuildJobExecutionCancellationRequestedEvent(
        Guid executionId,
        ContentEventImpact? impact = null,
        string? jobType = null)
    {
        return BuildJobExecutionStateChangedEvent(
            MagpieEvents.Names.JobExecutionCancellationRequested,
            executionId,
            impact,
            jobType);
    }

    public static MagpieEvent BuildJobExecutionCancelledEvent(
        Guid executionId,
        ContentEventImpact? impact = null,
        string? jobType = null)
    {
        return BuildJobExecutionStateChangedEvent(
            MagpieEvents.Names.JobExecutionCancelled,
            executionId,
            impact,
            jobType);
    }

    public static MagpieEvent BuildJobExecutionCompletedEvent(
        Guid executionId,
        ContentEventImpact? impact = null,
        string? jobType = null)
    {
        return BuildJobExecutionStateChangedEvent(
            MagpieEvents.Names.JobExecutionCompleted,
            executionId,
            impact,
            jobType);
    }

    public static MagpieEvent BuildJobExecutionCreatedEvent(
        Guid executionId,
        ContentEventImpact? impact = null,
        string? jobType = null)
    {
        return BuildJobExecutionStateChangedEvent(MagpieEvents.Names.JobExecutionCreated, executionId, impact, jobType);
    }

    public static MagpieEvent BuildJobExecutionFailedEvent(Guid executionId, string jobType, string failureReason)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExecutionId] = executionId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ExecutionJobType] = jobType ?? string.Empty,
            [MagpieEvents.Keys.ExecutionFailureReason] = failureReason ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.JobExecutionFailed, context, default, default, null);
    }

    public static MagpieEvent BuildJobExecutionProgressChangedEvent(
        Guid executionId,
        ContentEventImpact? impact = null,
        string? jobType = null)
    {
        return BuildJobExecutionStateChangedEvent(
            MagpieEvents.Names.JobExecutionProgressChanged,
            executionId,
            impact,
            jobType);
    }

    public static MagpieEvent BuildJobExecutionQueuedEvent(
        Guid executionId,
        ContentEventImpact? impact = null,
        string? jobType = null)
    {
        return BuildJobExecutionStateChangedEvent(MagpieEvents.Names.JobExecutionQueued, executionId, impact, jobType);
    }

    public static MagpieEvent BuildJobExecutionStartedEvent(
        Guid executionId,
        ContentEventImpact? impact = null,
        string? jobType = null)
    {
        return BuildJobExecutionStateChangedEvent(MagpieEvents.Names.JobExecutionStarted, executionId, impact, jobType);
    }

    public static MagpieEvent BuildJobExecutionStateChangedEvent(
        string name,
        Guid executionId,
        ContentEventImpact? impact,
        string? jobType)
    {
        var context = impact?.ToContext() ?? [];
        context[MagpieEvents.Keys.ExecutionId] = executionId.ToString("D", CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(jobType))
        {
            context[MagpieEvents.Keys.ExecutionJobType] = jobType!;
        }

        return new(Guid.Empty, name, context, default, default, null);
    }
}
