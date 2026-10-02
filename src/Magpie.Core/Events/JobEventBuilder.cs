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
            context[MagpieEvents.Keys.ExecutionJobType] = jobType;
        }

        return new(Guid.Empty, name, context, default, default, null);
    }
}
