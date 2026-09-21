using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class MetadataContainerCleanupJob(
    IMetadataContainerRepository metadataContainerRepository,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.MetadataContainerCleanup,
        DisplayName = "Metadata container cleanup",
        Description = "Removes metadata records that are no longer linked to content or subjects.",
        Category = "Metadata",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(10),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        progressReporter.Report(0, "The cleanup will delete metadata containers that have no owner.");
        var orphanedContainerIds = await metadataContainerRepository
            .ListOrphanedContainerIdsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (orphanedContainerIds.Count > 0)
        {
            await metadataContainerRepository
                .RemoveContainersAsync(orphanedContainerIds, cancellationToken)
                .ConfigureAwait(false);
        }

        progressReporter.Report(
            1, $"The metadata container cleanup completed. Deleted containers: {orphanedContainerIds.Count}.");
    }
}
