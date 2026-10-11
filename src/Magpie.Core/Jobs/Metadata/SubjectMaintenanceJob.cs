using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class SubjectMaintenanceJob(
    ISubjectRepository subjectRepository,
    IMetadataContainerRepository metadataContainerRepository,
    IContentFilterRepository contentFilterRepository,
    ISubjectFilterRepository subjectFilterRepository,
    ITransactionManager transactionManager,
    IProgressReporter progressReporter,
    ILogger<SubjectMaintenanceJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.SubjectMaintenance,
        DisplayName = "Subject maintenance",
        Description = "Merges subjects that share an external id, then removes subjects that have no content " +
            "relationships or user-specified metadata.",
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
        progressReporter.Report(0, "The subject maintenance will merge subjects that share an external id.");

        var merged = await MergeDuplicatesAsync(cancellationToken).ConfigureAwait(false);

        var subjectIds = await QueryIdsAsync(OrphanQuery(), cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Subject maintenance selected {CandidateCount} subject(s) to prune", subjectIds.Count);

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
            "Subject maintenance pruned {PrunedCount} of {CandidateCount} subject(s)",
            pruned,
            subjectIds.Count);
        progressReporter.Report(
            1,
            $"The subject maintenance completed. Merged subjects: {merged}. Deleted subjects: {pruned}.");
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

    private static bool RedirectContainerIds(
        ContentFilterCriteria criteria,
        IReadOnlySet<Guid> mergedContainerIds,
        Guid survivorContainerId) => criteria.ContentHierarchyItem.And
            .Count(item => RedirectContainerIds(item, mergedContainerIds, survivorContainerId)) > 0;

    private static bool RedirectContainerIds(
        SubjectFilterCriteria criteria,
        IReadOnlySet<Guid> mergedContainerIds,
        Guid survivorContainerId) => criteria.Metadata.And
            .Count(metadata => RedirectContainerIds(metadata, mergedContainerIds, survivorContainerId)) > 0;

    private static bool RedirectContainerIds(
        ContentHierarchyItemCriteria criteria,
        IReadOnlySet<Guid> mergedContainerIds,
        Guid survivorContainerId)
    {
        var metadataChanged = criteria.Metadata.And
            .Count(metadata => RedirectContainerIds(metadata, mergedContainerIds, survivorContainerId)) > 0;
        var subtreeChanged = criteria.Subtree.SomeFlat
            .Concat(criteria.Subtree.None)
            .Count(subtree => RedirectContainerIds(subtree, mergedContainerIds, survivorContainerId)) > 0;
        return metadataChanged | subtreeChanged;
    }

    private static bool RedirectContainerIds(
        MetadataCriteria criteria,
        IReadOnlySet<Guid> mergedContainerIds,
        Guid survivorContainerId)
    {
        var idsChanged = criteria.ContainerIds.Any(mergedContainerIds.Contains);
        if (idsChanged)
        {
            criteria.ContainerIds =
            [
                .. criteria.ContainerIds.Select(id => mergedContainerIds.Contains(id) ? survivorContainerId : id)
            ];
        }

        var relationshipsChanged = criteria.Relationships.SomeFlat
            .Concat(criteria.Relationships.None)
            .SelectMany(relationship => relationship.RelatedMetadataContainerId.And)
            .Count(criterion => RedirectContainerIds(criterion, mergedContainerIds, survivorContainerId)) > 0;
        return idsChanged | relationshipsChanged;
    }

    private static bool RedirectContainerIds(
        PropertyCriterion<Guid> criterion,
        IReadOnlySet<Guid> mergedContainerIds,
        Guid survivorContainerId)
    {
        var valueChanged = criterion.Value is { } value && mergedContainerIds.Contains(value);
        if (valueChanged)
        {
            criterion.Value = survivorContainerId;
        }

        var valuesChanged = criterion.Values?.Any(mergedContainerIds.Contains) == true;
        if (valuesChanged)
        {
            criterion.Values =
            [
                .. criterion.Values!.Select(id => mergedContainerIds.Contains(id) ? survivorContainerId : id)
            ];
        }

        return valueChanged | valuesChanged;
    }

    private static string[] SharedExternalIds(IReadOnlyList<Subject> subjects) =>
    [
        .. subjects
            .SelectMany(subject => subject.MetadataContainer!.Contributions
                .SelectMany(contribution => contribution.ExternalIds)
                .Where(externalId => externalId.Source is not null)
                .Select(externalId => (
                    Source: externalId.Source!.NormalizeExternalIdentifierName(),
                    Value: externalId.Value.NormalizeExternalIdentifierValue()))
                .Where(identity => identity.Source.Length > 0 && identity.Value.Length > 0)
                .Distinct())
            .GroupBy(identity => identity)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Source}:{group.Key.Value}")
    ];

    private async Task<int> MergeDuplicatesAsync(CancellationToken cancellationToken)
    {
        var duplicates = await subjectRepository.FindDuplicatesAsync(cancellationToken).ConfigureAwait(false);
        var mergedCount = 0;
        foreach (var group in duplicates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ordered = group.OrderBy(subject => subject.CreatedAt).ThenBy(subject => subject.Id).ToList();
            var survivor = ordered[0];
            var merged = ordered.Skip(1).ToList();
            List<Guid> mergedIds = [.. merged.Select(subject => subject.Id)];
            HashSet<Guid> mergedContainerIds = [.. merged.Select(subject => subject.MetadataContainerId)];
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    foreach (var filter in await contentFilterRepository.ListAllAsync(ct).ConfigureAwait(false))
                    {
                        if (RedirectContainerIds(filter.Criteria, mergedContainerIds, survivor.MetadataContainerId))
                        {
                            await contentFilterRepository.UpdateAsync(filter, null, ct).ConfigureAwait(false);
                        }
                    }

                    foreach (var filter in await subjectFilterRepository.ListAllAsync(ct).ConfigureAwait(false))
                    {
                        if (RedirectContainerIds(filter.Criteria, mergedContainerIds, survivor.MetadataContainerId))
                        {
                            await subjectFilterRepository.UpdateAsync(filter, null, ct).ConfigureAwait(false);
                        }
                    }

                    await subjectRepository.MergeAsync(survivor.Id, mergedIds, ct).ConfigureAwait(false);
                    await subjectRepository.DeleteWhereAsync(new SubjectQuery { Ids = mergedIds }, ct)
                        .ConfigureAwait(false);
                    await metadataContainerRepository
                        .RemoveContainersAsync([.. merged.Select(subject => subject.MetadataContainerId)], ct)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Subject maintenance merged subjects {MergedSubjectIds} into subject {SurvivorSubjectId} " +
                "through shared external ids {SharedExternalIds}",
                mergedIds,
                survivor.Id,
                SharedExternalIds(group));
            mergedCount += mergedIds.Count;
        }

        return mergedCount;
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
