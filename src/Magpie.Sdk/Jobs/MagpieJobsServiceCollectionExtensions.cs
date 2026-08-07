using System;
using System.Linq;

using Magpie.Models;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency injection extensions for Magpie job handlers.
/// </summary>
public static class MagpieJobsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a job handler and its specification with the job runtime.
    /// </summary>
    /// <typeparam name="THandler">The type of the job handler</typeparam>
    /// <param name="services">The service collection of the host</param>
    /// <param name="spec">The specification of the job</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection RegisterJob<THandler>(this IServiceCollection services, JobSpec spec)
        where THandler : class, IJob
    {
#if NETSTANDARD2_1
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }
#else
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(spec);
#endif

        if (string.IsNullOrWhiteSpace(spec.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(spec));
        }

        services.TryAddScoped<THandler>();

        if (!services.Any(IsDuplicateRegistration))
        {
            services.AddSingleton(new JobRegistration(spec, typeof(THandler)));
        }

        return services;

        bool IsDuplicateRegistration(ServiceDescriptor descriptor)
        {
            return descriptor.ServiceType == typeof(JobRegistration) &&
                descriptor.ImplementationInstance is JobRegistration registration &&
                string.Equals(registration.Spec.JobType, spec.JobType, StringComparison.Ordinal) &&
                registration.HandlerType == typeof(THandler);
        }
    }
}
