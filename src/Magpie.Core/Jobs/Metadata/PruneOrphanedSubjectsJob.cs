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
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class PruneOrphanedSubjectsJob(
    ISubjectRepository subjectRepository,
    ISubjectService subjectService,
    ITransactionManager transactionManager,
    IProgressReporter progressReporter,
    ILogger<PruneOrphanedSubjectsJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "PruneOrphanedSubjects",
        DisplayName = "Prune orphaned subjects",
        Description = "Removes subjects that have no content relationships or user-specified metadata.",
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
        progressReporter.Report(0, "The cleanup will find subjects that have no owner.");
        const int pageSize = 500;
        var subjectIds = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await subjectRepository.QueryAsync(
                OrphanQuery(),
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            subjectIds.AddRange(pageItems.Select(subject => subject.Id));
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        var contentLinkedSubjectIds = await QueryIdsAsync(
            new() { HasContentRelationships = true },
            cancellationToken).ConfigureAwait(false);
        var userContributedSubjectIds = await QueryIdsAsync(
            new() { HasUserContributions = true },
            cancellationToken).ConfigureAwait(false);
        var taggedSubjectIds = await QueryIdsAsync(
            new() { HasTags = true },
            cancellationToken).ConfigureAwait(false);
        var userCreatedSubjectIds = await QueryIdsAsync(
            new() { IsUserCreated = true },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Subject pruning selected {CandidateCount} candidate(s)", subjectIds.Count);
        logger.LogInformation(
            "Subject pruning skipped {SkippedCount} subject(s) with content relationships. Subject ids: {SubjectIds}",
            contentLinkedSubjectIds.Count,
            string.Join(", ", contentLinkedSubjectIds));
        logger.LogInformation(
            "Subject pruning skipped {SkippedCount} subject(s) with user contributions. Subject ids: {SubjectIds}",
            userContributedSubjectIds.Count,
            string.Join(", ", userContributedSubjectIds));
        logger.LogInformation(
            "Subject pruning skipped {SkippedCount} subject(s) with user tags. Subject ids: {SubjectIds}",
            taggedSubjectIds.Count,
            string.Join(", ", taggedSubjectIds));
        logger.LogInformation(
            "Subject pruning skipped {SkippedCount} subject(s) created by real users. Subject ids: {SubjectIds}",
            userCreatedSubjectIds.Count,
            string.Join(", ", userCreatedSubjectIds));

        var pruned = 0;
        foreach (var subjectId in subjectIds)
        {
            var result = await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var stillPrunable = false;
                    var pageNumber = 1;
                    while (!stillPrunable)
                    {
                        var page = await subjectRepository.QueryAsync(
                            OrphanQuery(),
                            new() { PageNumber = pageNumber, PageSize = pageSize },
                            null,
                            ct).ConfigureAwait(false);
                        var pageItems = page.Items.ToList();
                        stillPrunable = pageItems.Any(subject => subject.Id == subjectId);
                        if (stillPrunable || pageItems.Count < pageSize)
                        {
                            break;
                        }

                        pageNumber++;
                    }

                    if (!stillPrunable)
                    {
                        return (StillPrunable: false, Deleted: false);
                    }

                    var deleted = await subjectService.DeleteAsync(subjectId, null, ct).ConfigureAwait(false);
                    return (StillPrunable: true, Deleted: deleted);
                },
                cancellationToken).ConfigureAwait(false);

            if (result.Deleted)
            {
                pruned++;
            }
            else if (!result.StillPrunable)
            {
                logger.LogInformation(
                    "Subject pruning skipped candidate {SubjectId} because it no longer qualifies",
                    subjectId);
            }
            else
            {
                logger.LogInformation(
                    "Subject pruning skipped candidate {SubjectId} because it no longer exists",
                    subjectId);
            }
        }

        logger.LogInformation("Subject pruning removed {PrunedCount} subject(s)", pruned);
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

    private async Task<IReadOnlyList<Guid>> QueryIdsAsync(
        SubjectQuery query,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var ids = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await subjectRepository.QueryAsync(
                query,
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            ids.AddRange(pageItems.Select(subject => subject.Id));
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        return ids;
    }
}
