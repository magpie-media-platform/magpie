using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Metadata;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing metadata containers and contributions.
/// </summary>
public interface IMetadataRepository
{
    /// <summary>
    /// Get a metadata container by ID with all its contributions.
    /// </summary>
    /// <param name="id">The container identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<MetadataContainer?>> GetContainerAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a contribution to a metadata container.
    /// </summary>
    /// <param name="containerId">The container identifier.</param>
    /// <param name="contribution">The contribution to add.</param>
    /// <param name="createdByUserId">The id of the user creating the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<MetadataContribution>> AddContributionAsync(
        Guid containerId,
        MetadataContribution contribution,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patch a metadata contribution with partial updates.
    /// </summary>
    /// <param name="patch">The patch object containing the updates.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the contribution was patched,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> PatchContributionAsync(
        MetadataContributionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a metadata contribution by ID.
    /// </summary>
    /// <param name="id">The contribution identifier.</param>
    /// <param name="deletedByUserId">The id of the user deleting the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the contribution was deleted,
    /// otherwise <see langword="false"/>. Returns a failure result if the contribution is locked.
    /// </returns>
    Task<Result<bool>> DeleteContributionAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
