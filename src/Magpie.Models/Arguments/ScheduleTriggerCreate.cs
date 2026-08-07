using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for a schedule trigger.
/// </summary>
public sealed record ScheduleTriggerCreate
{
    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.ExtensionId" path="/summary"/>
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.JobType" path="/summary"/>
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.Scope" path="/summary"/>
    /// </summary>
    public required JobScope Scope { get; set; }

    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.Name" path="/summary"/>
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.Schedule" path="/summary"/>
    /// </summary>
    public required string Schedule { get; set; }

    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.ExecutionMode" path="/summary"/>
    /// </summary>
    public JobExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.Enabled" path="/summary"/>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <inheritdoc cref="ScheduleTrigger.Parameters" path="/summary"/>
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = [];
}
