using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A running or completed job execution.
/// </summary>
public record JobExecution
{
    /// <summary>
    /// The unique id of the job execution.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the schedule trigger.
    /// </summary>
    public Guid? ScheduleTriggerId { get; set; }

    /// <summary>
    /// The id of the event trigger.
    /// </summary>
    public Guid? EventTriggerId { get; set; }

    /// <summary>
    /// The id of the extension that defines the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The type of the job.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The scope of the job.
    /// </summary>
    public JobScope Scope { get; set; }

    /// <summary>
    /// The context values from the trigger.
    /// </summary>
    public Dictionary<string, string> TriggerContext { get; set; } = [];

    /// <summary>
    /// The parameters of the job.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = [];

    /// <summary>
    /// The execution mode of the job.
    /// </summary>
    public JobExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// The progress of the execution.
    /// </summary>
    public double? Progress { get; set; }

    /// <summary>
    /// The status of the execution.
    /// </summary>
    public JobExecutionStatus Status { get; set; }

    /// <summary>
    /// The message that describes the current status.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// The id of the node that runs this execution.
    /// </summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// The UTC time of the last heartbeat.
    /// </summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// The earliest UTC time the execution can start.
    /// </summary>
    public DateTime? NotBeforeAt { get; set; }

    /// <summary>
    /// The number of the current attempt.
    /// </summary>
    public int? Attempt { get; set; }

    /// <summary>
    /// The id of the original execution.
    /// </summary>
    public Guid? OriginalExecutionId { get; set; }

    /// <summary>
    /// The id of the parent execution.
    /// </summary>
    public Guid? ParentExecutionId { get; set; }

    /// <summary>
    /// The correlation id that links related executions.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// The name of the lock partition of the execution.
    /// </summary>
    public string? LockPartition { get; set; }

    /// <summary>
    /// The key that deduplicates queued executions.
    /// </summary>
    public string? DedupKey { get; set; }

    /// <summary>
    /// The UTC time the execution started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// The UTC time the execution completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The UTC time of the cancel request.
    /// </summary>
    public DateTime? CancellationRequestedAt { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}
