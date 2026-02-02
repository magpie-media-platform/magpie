using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing metadata containers and contributions.
/// </summary>
public interface IMetadataContainerRepository
{
    /// <summary>
    /// Get a metadata container by id.
    /// </summary>
    /// <param name="id">The container id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<MetadataContainer?> GetContainerAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a contribution to a metadata container.
    /// </summary>
    /// <param name="containerId">The container id.</param>
    /// <param name="contribution">The contribution to add.</param>
    /// <param name="createdByUserId">The id of the user creating the contribution.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<MetadataContribution> AddContributionAsync(
        Guid containerId,
        MetadataContribution contribution,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a metadata contribution by id.
    /// </summary>
    /// <param name="containerId">The metadata container id.</param>
    /// <param name="contributionId">The metadata contribution id.</param>
    /// <param name="deletedByUserId">The id of the user deleting the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the contribution was deleted; otherwise, <see langword="false"/>.</returns>
    Task<bool> RemoveContributionAsync(
        Guid containerId,
        Guid contributionId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
