using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Jobs;

/// <summary>
/// The request to queue a job or to run it immediately.
/// </summary>
public sealed record JobEnqueueRequest
{
    /// <summary>
    /// The name of the job type to run.
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// The id of the extension that owns the job type.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The parameters for the job, keyed by parameter name.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>
    /// How the job runs, on the queue or immediately.
    /// </summary>
    public JobExecutionMode Mode { get; init; } = JobExecutionMode.Queue;

    /// <summary>
    /// The id of the user that started the job.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// The id that groups related job executions.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// The earliest UTC time when the job can run.
    /// </summary>
    public DateTime? NotBeforeAt { get; init; }

    /// <summary>
    /// Whether a coalesced request clears the deferral of the existing job.
    /// </summary>
    public bool ClearDeferralOnCoalesce { get; init; }

    /// <summary>
    /// The id of the event trigger that started the job.
    /// </summary>
    public Guid? EventTriggerId { get; init; }

    /// <summary>
    /// The trigger context to copy onto the job execution.
    /// </summary>
    public IReadOnlyDictionary<string, string>? TriggerContext { get; init; }
}
