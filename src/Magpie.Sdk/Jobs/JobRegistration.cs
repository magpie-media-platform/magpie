using System;

using Magpie.Models;

namespace Magpie.Sdk.Jobs;

/// <summary>
/// The binding of a job handler type to its specification.
/// </summary>
/// <param name="Spec">The specification of the job</param>
/// <param name="HandlerType">The type of the job handler</param>
public sealed record JobRegistration(JobSpec Spec, Type HandlerType);
