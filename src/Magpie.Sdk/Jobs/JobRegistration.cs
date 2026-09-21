using System;

using Magpie.Models;

namespace Magpie.Sdk.Jobs;

/// <summary>
/// The binding of a job handler type to its specification.
/// </summary>
/// <param name="Spec">The specification of the job</param>
/// <param name="HandlerType">The type of the job handler</param>
public sealed record JobRegistration(JobSpec Spec, Type HandlerType)
{
    /// <summary>
    /// The specification of the job.
    /// </summary>
    public JobSpec Spec { get; set; } = Spec;

    /// <summary>
    /// The type of the job handler.
    /// </summary>
    public Type HandlerType { get; set; } = HandlerType;
}
