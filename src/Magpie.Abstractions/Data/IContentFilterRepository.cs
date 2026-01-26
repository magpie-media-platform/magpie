using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Filters;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing content filters.
/// </summary>
public interface IContentFilterRepository :
    ICrudRepository<Guid, ContentFilter>,
    IPatchRepository<Guid, ContentFilter, ContentFilterPatch>
{
    /// <summary>
    /// Get a content filter by name.
    /// </summary>
    /// <param name="name">The filter name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ContentFilter?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all content filters that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<ContentFilter>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all content filters created by a specific user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<ContentFilter>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
