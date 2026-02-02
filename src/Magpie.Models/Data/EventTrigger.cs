using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A trigger that runs a job in response to an event.
/// </summary>
public record EventTrigger
{
    /// <summary>
    /// The event trigger id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The extension that defines the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The job type.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The job scope.
    /// </summary>
    public required JobScope Scope { get; set; }

    /// <summary>
    /// The execution mode for this trigger.
    /// </summary>
    public JobExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// The filter for triggering events.
    /// </summary>
    /// <remarks>
    /// The event filter is stored as serialized JSON.
    /// </remarks>
    public required EventFilter EventFilter { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

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
