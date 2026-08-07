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
    /// The unique id of the event trigger.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the extension that defines the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The type of the job to run.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The scope of the job.
    /// </summary>
    public required JobScope Scope { get; set; }

    /// <summary>
    /// The filter that selects the events for the trigger.
    /// </summary>
    public required EventFilter EventFilter { get; set; }

    /// <summary>
    /// The display name of the event trigger.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether the event trigger is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The number of seconds to delay the job after the event fires.
    /// </summary>
    public int DelaySeconds { get; set; }

    /// <summary>
    /// The parameters for the job.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = [];

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

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
