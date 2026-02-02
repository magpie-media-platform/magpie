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
    /// The job execution id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The schedule trigger that initiated this execution, if applicable.
    /// </summary>
    public Guid? ScheduleTriggerId { get; set; }

    /// <summary>
    /// The event trigger that initiated this execution, if applicable.
    /// </summary>
    public Guid? EventTriggerId { get; set; }

    /// <summary>
    /// The extension that defines the job provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The job type.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The job scope.
    /// </summary>
    public JobScope Scope { get; set; }

    /// <summary>
    /// The context of the trigger that initiated this job.
    /// </summary>
    public Dictionary<string, string> TriggerContext { get; set; } = [];

    /// <summary>
    /// Parameters that were passed to the job.
    /// </summary>
    /// <remarks>
    /// The parameters are stored as serialized JSON.
    /// </remarks>
    public Dictionary<string, string> Parameters { get; set; } = [];

    /// <summary>
    /// The progress percentage (0-100).
    /// </summary>
    public double? Progress { get; set; }

    /// <summary>
    /// The execution status.
    /// </summary>
    public JobExecutionStatus Status { get; set; }

    /// <summary>
    /// A status message.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// The node executing this job.
    /// </summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// The last heartbeat from the executing node.
    /// </summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// When the execution will time out.
    /// </summary>
    public DateTime? TimeoutAt { get; set; }

    /// <summary>
    /// When execution started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When execution completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}
