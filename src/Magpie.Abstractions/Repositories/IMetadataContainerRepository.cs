using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for metadata containers and their contributions.
/// </summary>
public interface IMetadataContainerRepository : IPatchRepository<Guid, MetadataContainer, MetadataContainerPatch>
{
    /// <summary>
    /// Adds a metadata contribution to a container.
    /// </summary>
    /// <param name="containerId">The id of the metadata container</param>
    /// <param name="contribution">The contribution to add</param>
    /// <param name="createdByUserId">
    /// The id of the user that adds the contribution, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new contribution</returns>
    Task<MetadataContribution> AddContributionAsync(
        Guid containerId,
        MetadataContribution contribution,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a stored contribution with new data.
    /// </summary>
    /// <param name="containerId">The id of the metadata container</param>
    /// <param name="existingContributionId">The id of the contribution to replace</param>
    /// <param name="incoming">The contribution data that replaces the stored data</param>
    /// <param name="modifiedByUserId">
    /// The id of the user that changes the contribution, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The contribution with its new values</returns>
    Task<MetadataContribution> UpdateContributionAsync(
        Guid containerId,
        Guid existingContributionId,
        MetadataContribution incoming,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the primary image URL of each metadata contribution in a set.
    /// </summary>
    /// <param name="contributionIds">The ids of the metadata contributions to look up</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The primary image URLs</returns>
    Task<IReadOnlyList<Uri>> ListPrimaryImageUrlsAsync(
        IReadOnlyCollection<Guid> contributionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the relationship keys of one metadata contribution.
    /// </summary>
    /// <param name="contributionId">The id of the metadata contribution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The stored relationship keys</returns>
    Task<IReadOnlyList<MetadataRelationshipKey>> ListRelationshipKeysAsync(
        Guid contributionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a relationship to a metadata contribution.
    /// </summary>
    /// <param name="relationship">The relationship to add</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task AddRelationshipAsync(
        Relationship relationship,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a set of metadata contributions and their metadata graph.
    /// </summary>
    /// <param name="contributions">The metadata contributions to remove</param>
    /// <param name="deletedByUserId">
    /// The id of the user that removes the contributions, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="correlationId">The id that links this change to the operation that caused it</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RemoveContributionsAsync(
        IReadOnlyList<MetadataContribution> contributions,
        Guid? deletedByUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the ids of the metadata containers that nothing references.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The ids of the orphaned metadata containers</returns>
    Task<IReadOnlyList<Guid>> ListOrphanedContainerIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a set of metadata containers and their metadata graph.
    /// </summary>
    /// <param name="containerIds">The ids of the metadata containers to remove</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RemoveContainersAsync(
        IReadOnlyList<Guid> containerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the titles of the related subjects of a set of content hierarchy items, by relationship kind.
    /// </summary>
    /// <param name="contentHierarchyItemIds">The ids of the content hierarchy items to look up</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The titles for each content hierarchy item id, then for each relationship kind</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>
        GetRelationshipTitlesByContentHierarchyItemAsync(
            IReadOnlyCollection<Guid> contentHierarchyItemIds,
            CancellationToken cancellationToken = default);
}
