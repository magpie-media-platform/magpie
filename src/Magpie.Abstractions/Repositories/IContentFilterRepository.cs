using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

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
    Task<ContentFilter?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all content filters that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<ContentFilter>> ListByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all content filters with a specific user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<ContentFilter>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
