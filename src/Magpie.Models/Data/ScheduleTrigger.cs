using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A schedule-based trigger for running jobs.
/// </summary>
public record ScheduleTrigger
{
    /// <summary>
    /// The schedule trigger id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The extension that defines the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The job type to execute.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The job classification.
    /// </summary>
    public required JobScope Scope { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The cron schedule expression.
    /// </summary>
    public required string Schedule { get; set; }

    /// <summary>
    /// The execution mode for this trigger.
    /// </summary>
    public JobExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// Whether the trigger is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Parameters to pass to the job.
    /// </summary>
    /// <remarks>
    /// The parameters are stored as serialized JSON.
    /// </remarks>
    public Dictionary<string, string> Parameters { get; set; } = [];

    /// <summary>
    /// When the trigger last ran.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// When the trigger is scheduled to run next.
    /// </summary>
    public DateTime? NextRunAt { get; set; }

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

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
