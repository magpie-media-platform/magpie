using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

namespace Magpie.Core.Content;

internal sealed class MetadataService(
    IMaterializedMetadataRepository materializedRepository,
    IMetadataContainerRepository containerRepository,
    ISubjectRepository subjectRepository,
    IContentHierarchyItemRepository contentRepository,
    IEventBus eventBus,
    ITransactionManager transactionManager) : IMetadataService
{
    public async Task<MaterializedMetadata?> GetContentMetadataAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        var result = await materializedRepository.QueryAsync(
            new() { ContainerIds = [item.MetadataContainerId] },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        return result.Items.FirstOrDefault();
    }

    public async Task<IReadOnlyList<MaterializedMetadata>> GetRelatedMetadataAsync(
        Guid contentHierarchyItemId,
        IReadOnlyCollection<string> relationshipKinds,
        CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null || relationshipKinds.Count == 0)
        {
            return [];
        }

        var metadataPage = await materializedRepository.QueryAsync(
            new() { ContainerIds = [item.MetadataContainerId] },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        var metadata = metadataPage.Items.FirstOrDefault();
        var ids = (metadata?.Relationships ?? [])
            .Where(relationship => relationship.RelatedMetadataContainerId is not null &&
                relationshipKinds.Contains(relationship.Kind, StringComparer.Ordinal))
            .Select(relationship => relationship.RelatedMetadataContainerId!.Value)
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var distinctIds = ids.Distinct().ToArray();
        var related = await materializedRepository.QueryAsync(
            new() { ContainerIds = distinctIds },
            new() { PageNumber = 1, PageSize = distinctIds.Length },
            null,
            cancellationToken).ConfigureAwait(false);
        var byId = related.Items.ToDictionary(value => value.ContainerId);
        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    public async Task<IReadOnlyList<MetadataContribution>?> GetContentContributionsAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        return item is null
            ? null
            : await GetContributionsAsync(item.MetadataContainerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PatchContentContributionAsync(
        Guid contentHierarchyItemId,
        MetadataContainerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null || !await containerRepository.PatchAsync(
            patch with { ContainerId = item.MetadataContainerId },
            actingUserId,
            cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await transactionManager.OnCommittedAsync(
            token => eventBus.PublishAsync(
                new(
                    Guid.Empty,
                    MagpieEvents.Names.MetadataRefreshed,
                    new ContentEventImpact
                    {
                        HierarchyItemId = item.Id,
                        MetadataContainerId = item.MetadataContainerId,
                        ParentId = item.ParentId,
                        ContentType = item.ContentType,
                        Level = item.Level,
                        TopLevel = item.ParentId is null,
                        RootFolderId = item.RootFolderId,
                        QualityTierListId = item.QualityTierListId,
                        Monitoring = item.EffectiveMonitoring,
                        MetadataSourceContainerId = item.MetadataContainerId
                    }.ToContext(),
                    default,
                    default,
                    null),
                token),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<MaterializedMetadata?> GetSubjectMetadataAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        var subject = await subjectRepository.GetByIdAsync(subjectId, cancellationToken).ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        var result = await materializedRepository.QueryAsync(
            new() { ContainerIds = [subject.MetadataContainerId] },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        return result.Items.FirstOrDefault();
    }

    public async Task<IReadOnlyList<MetadataContribution>?> GetSubjectContributionsAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        var subject = await subjectRepository.GetByIdAsync(subjectId, cancellationToken).ConfigureAwait(false);
        return subject is null
            ? null
            : await GetContributionsAsync(subject.MetadataContainerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PatchSubjectContributionAsync(
        Guid subjectId,
        MetadataContainerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var subject = await subjectRepository.GetByIdAsync(subjectId, cancellationToken).ConfigureAwait(false);
        return subject is not null && await containerRepository.PatchAsync(
            patch with { ContainerId = subject.MetadataContainerId },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MetadataContribution>?> GetContributionsAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        var container = await containerRepository.GetByIdAsync(containerId, cancellationToken).ConfigureAwait(false);
        return container is null ? null : [.. container.Contributions.OrderBy(value => value.Ordinal)];
    }
}
