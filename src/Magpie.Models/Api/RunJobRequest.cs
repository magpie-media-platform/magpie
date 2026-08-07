using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The request to run a job.
/// </summary>
public sealed record RunJobRequest
{
    /// <summary>
    /// The id of the job type.
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// The id of the extension that provides the job.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The parameters for the job run, keyed by parameter name.
    /// </summary>
    public Dictionary<string, string>? Parameters { get; init; }

    /// <summary>
    /// The execution mode for the job run.
    /// </summary>
    public JobExecutionMode Mode { get; init; } = JobExecutionMode.Queue;
}
