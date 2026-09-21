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
    public required string JobType { get; set; }

    /// <summary>
    /// The id of the extension that owns the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The parameters for the job, keyed by parameter name.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; set; }

    /// <summary>
    /// How the job runs, on the queue or immediately.
    /// </summary>
    public JobExecutionMode Mode { get; set; } = JobExecutionMode.Queue;

    /// <summary>
    /// The id of the user that started the job.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// The id that groups related job executions.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// Whether the new execution follows the execution that made the request and is not a child of it.
    /// </summary>
    public bool IsSuccessor { get; set; }

    /// <summary>
    /// The earliest UTC time when the job can run.
    /// </summary>
    public DateTime? NotBeforeAt { get; set; }

    /// <summary>
    /// The id of the event trigger that started the job.
    /// </summary>
    public Guid? EventTriggerId { get; set; }

    /// <summary>
    /// The trigger context to copy onto the job execution.
    /// </summary>
    public IReadOnlyDictionary<string, string>? TriggerContext { get; set; }
}
