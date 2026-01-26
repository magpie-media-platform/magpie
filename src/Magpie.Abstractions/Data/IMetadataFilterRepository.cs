using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Filters;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing metadata filters.
/// </summary>
public interface IMetadataFilterRepository :
    ICrudRepository<Guid, MetadataFilter>,
    IPatchRepository<Guid, MetadataFilter, MetadataFilterPatch>
{
    /// <summary>
    /// Get a metadata filter by name.
    /// </summary>
    /// <param name="name">The filter name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<MetadataFilter?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all metadata filters that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MetadataFilter>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default);
}
