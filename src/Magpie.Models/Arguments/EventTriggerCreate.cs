using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for an event trigger.
/// </summary>
public sealed record EventTriggerCreate
{
    /// <summary>
    /// The id of the extension that defines the job type.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The type of job that the trigger runs.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// The scope that the job runs in.
    /// </summary>
    public required JobScope Scope { get; set; }

    /// <summary>
    /// The display name of the trigger.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The <see cref="EventFilter"/> that activates the trigger.
    /// </summary>
    public required EventFilter EventFilter { get; set; }

    /// <summary>
    /// Whether the trigger is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The number of seconds to defer job eligibility after the event fires.
    /// </summary>
    public int DelaySeconds { get; set; }

    /// <summary>
    /// The parameters for the triggered job.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = [];
}
