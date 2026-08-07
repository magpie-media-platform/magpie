using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A schedule-based trigger for a job.
/// </summary>
public record ScheduleTrigger
{
    /// <summary>
    /// The unique id of the schedule trigger.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the extension that defines the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The name of the job type to start.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The classification of the job, from <see cref="JobScope"/>.
    /// </summary>
    public required JobScope Scope { get; set; }

    /// <summary>
    /// The name of the trigger to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The cron expression that sets the run times.
    /// </summary>
    public required string Schedule { get; set; }

    /// <summary>
    /// The way the job runs, from <see cref="JobExecutionMode"/>.
    /// </summary>
    public JobExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// Whether the trigger is in use.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The parameters for the job, keyed by name.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = [];

    /// <summary>
    /// The UTC time of the last run.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// The UTC time of the next run.
    /// </summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
