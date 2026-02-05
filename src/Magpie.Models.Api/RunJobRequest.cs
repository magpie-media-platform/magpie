using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// Request to run a job.
/// </summary>
public sealed record RunJobRequest
{
    /// <summary>
    /// The job type identifier.
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// The extension that provides the job.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The job parameters.
    /// </summary>
    public Dictionary<string, string>? Parameters { get; init; }

    /// <summary>
    /// The execution mode.
    /// </summary>
    public JobExecutionMode Mode { get; init; } = JobExecutionMode.Queue;
}
