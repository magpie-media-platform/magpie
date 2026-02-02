using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Arguments for creating a schedule trigger.
/// </summary>
public record ScheduleTriggerCreate
{
    /// <summary>
    /// The extension that defines the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The job type to execute.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The job scope.
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
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Parameters to pass to the job.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = [];
}
