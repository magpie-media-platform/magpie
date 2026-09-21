using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Magpie.Abstractions.Jobs;
using Magpie.Models;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class JobRegistry : IJobRegistry
{
    private readonly Dictionary<string, JobRegistration> _registrations;
    private readonly IReadOnlyList<JobSpec> _specs;

    public JobRegistry(IEnumerable<JobRegistration> registrations, ILogger<JobRegistry> logger)
    {
        _registrations = new();
        foreach (var registration in registrations)
        {
            if (_registrations.TryGetValue(registration.Spec.JobType, out var existing))
            {
                logger.LogWarning(
                    "Multiple jobs declared JobType {JobType}, so the registry will use {Winner} and ignore" +
                    " {Displaced}",
                    registration.Spec.JobType,
                    registration.HandlerType.FullName,
                    existing.HandlerType.FullName);
            }

            _registrations[registration.Spec.JobType] = registration;
        }

        _specs =
        [
            .. _registrations.Values
                .Select(registration => registration.Spec)
                .OrderBy(spec => spec.Category, StringComparer.Ordinal)
                .ThenBy(spec => spec.DisplayName, StringComparer.Ordinal)
                .ThenBy(spec => spec.JobType, StringComparer.Ordinal)
        ];
    }

    public bool TryGetJobSpec(string jobType, [MaybeNullWhen(false)] out JobSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(jobType) && _registrations.TryGetValue(jobType, out var registration))
        {
            spec = registration.Spec;
            return true;
        }

        spec = null;
        return false;
    }

    public IReadOnlyList<JobSpec> ListJobSpecs()
    {
        return _specs;
    }

    public bool TryGetHandlerType(JobSpec spec, [MaybeNullWhen(false)] out Type handlerType)
    {
        if (_registrations.TryGetValue(spec.JobType, out var registration))
        {
            handlerType = registration.HandlerType;
            return true;
        }

        handlerType = null;
        return false;
    }
}
