using System;
using System.Collections.Generic;

namespace Magpie.Models.Jobs;

/// <summary>
/// A job template provided by an extension.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">A description of what the job does.</param>
/// <param name="Provider">The extension that provides this job.</param>
/// <param name="Parameters">The parameters this job accepts.</param>
/// <param name="OnDemand">Whether the job can be run on demand.</param>
public sealed record JobTemplate(
    Guid Id,
    string Name,
    string? Description,
    string Provider,
    IReadOnlyList<JobParameterTemplate> Parameters,
    bool OnDemand);
