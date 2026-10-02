using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class PruneOrphanedSubjectsJob(
    ISubjectRepository subjectRepository,
    IMetadataContainerRepository metadataContainerRepository,
    ITransactionManager transactionManager,
    IProgressReporter progressReporter,
    ILogger<PruneOrphanedSubjectsJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.PruneOrphanedSubjects,
        DisplayName = "Prune orphaned subjects",
        Description = "Removes subjects that have no content relationships or user-specified metadata.",
        Category = "Metadata",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private const int PageSize = 500;

    private const int DeleteBatchSize = 100;

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        progressReporter.Report(0, "The cleanup will find subjects that have no owner.");

        var subjectIds = await QueryIdsAsync(OrphanQuery(), cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Subject pruning selected {CandidateCount} candidate(s)", subjectIds.Count);

        var pruned = 0;
        foreach (var batch in subjectIds.Chunk(DeleteBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            pruned += await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var page = await subjectRepository.QueryAsync(
                        OrphanQuery() with { Ids = batch },
                        new() { PageNumber = 1, PageSize = DeleteBatchSize },
                        null,
                        ct).ConfigureAwait(false);
                    var prunable = page.Items.ToList();
                    if (prunable.Count == 0)
                    {
                        return 0;
                    }

                    var prunableIds = prunable.Select(subject => subject.Id).ToList();
                    var deleted = await subjectRepository.DeleteWhereAsync(
                        OrphanQuery() with { Ids = prunableIds },
                        ct).ConfigureAwait(false);

                    var containerIds = prunable.Select(subject => subject.MetadataContainerId).ToList();
                    if (deleted < prunable.Count)
                    {
                        var survivors = await subjectRepository.QueryAsync(
                            new SubjectQuery { Ids = prunableIds },
                            new() { PageNumber = 1, PageSize = prunable.Count },
                            null,
                            ct).ConfigureAwait(false);
                        var survivingContainerIds = survivors.Items
                            .Select(subject => subject.MetadataContainerId)
                            .ToHashSet();
                        containerIds = [.. containerIds.Where(id => !survivingContainerIds.Contains(id))];
                    }

                    await metadataContainerRepository.RemoveContainersAsync(containerIds, ct)
                        .ConfigureAwait(false);

                    return deleted;
                },
                cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Subject pruning removed {PrunedCount} of {CandidateCount} candidate subject(s)",
            pruned,
            subjectIds.Count);
        progressReporter.Report(1, $"The subject cleanup completed. Deleted subjects: {pruned}.");
    }

    private static SubjectQuery OrphanQuery()
    {
        return new()
        {
            HasContentRelationships = false,
            HasUserContributions = false,
            HasTags = false,
            IsUserCreated = false
        };
    }

    private async Task<IReadOnlyList<Guid>> QueryIdsAsync(SubjectQuery query, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await subjectRepository.QueryAsync(
                query,
                new() { PageNumber = pageNumber, PageSize = PageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            ids.AddRange(pageItems.Select(subject => subject.Id));
            if (pageItems.Count < PageSize)
            {
                break;
            }

            pageNumber++;
        }

        return ids;
    }
}
