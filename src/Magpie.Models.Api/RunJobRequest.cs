using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// Request to run a job.
/// </summary>
/// <param name="JobType">The job type identifier.</param>
/// <param name="ExtensionId">The extension that provides the job.</param>
/// <param name="Parameters">The job parameters.</param>
/// <param name="Mode">The execution mode.</param>
public sealed record RunJobRequest(
    string JobType,
    Guid? ExtensionId,
    Dictionary<string, string>? Parameters,
    JobExecutionMode Mode = JobExecutionMode.Queue);
