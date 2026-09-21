using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for stored metadata.
/// </summary>
public interface IMetadataService
{
    /// <summary>
    /// Gets the materialized metadata of a content hierarchy item, or <see langword="null"/> when absent.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MaterializedMetadata?> GetContentMetadataAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the materialized metadata related to a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to start from</param>
    /// <param name="relationshipKinds">The relationship kinds to follow</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<MaterializedMetadata>> GetRelatedMetadataAsync(
        Guid contentHierarchyItemId,
        IReadOnlyCollection<string> relationshipKinds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the per-provider metadata contributions of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The contributions, or <see langword="null"/> when the item is absent</returns>
    Task<IReadOnlyList<MetadataContribution>?> GetContentContributionsAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches the local metadata contribution of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to patch</param>
    /// <param name="patch">The fields to change in the local contribution</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchContentContributionAsync(
        Guid contentHierarchyItemId,
        MetadataContainerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the materialized metadata of a subject, or <see langword="null"/> when absent.
    /// </summary>
    /// <param name="subjectId">The id of the subject to read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MaterializedMetadata?> GetSubjectMetadataAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the per-provider metadata contributions of a subject, or <see langword="null"/> when absent.
    /// </summary>
    /// <param name="subjectId">The id of the subject to read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<MetadataContribution>?> GetSubjectContributionsAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches the local metadata contribution of a subject.
    /// </summary>
    /// <param name="subjectId">The id of the subject to patch</param>
    /// <param name="patch">The fields to change in the local contribution</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchSubjectContributionAsync(
        Guid subjectId,
        MetadataContainerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);
}
