using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Magpie.Models;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// The catalog of the job types that Magpie can run.
/// </summary>
public interface IJobRegistry
{
    /// <summary>
    /// Gets the specification that is registered for a job type.
    /// </summary>
    /// <param name="jobType">The name of the job type to find</param>
    /// <param name="spec">The registered specification, set only when the method returns true</param>
    bool TryGetJobSpec(string jobType, [MaybeNullWhen(false)] out JobSpec spec);

    /// <summary>
    /// Gets every registered job specification.
    /// </summary>
    IReadOnlyList<JobSpec> ListJobSpecs();

    /// <summary>
    /// Gets the handler type that is registered for a job specification.
    /// </summary>
    /// <param name="spec">The job specification to find</param>
    /// <param name="handlerType">The registered handler type, set only when the method returns true</param>
    bool TryGetHandlerType(JobSpec spec, [MaybeNullWhen(false)] out Type handlerType);
}
