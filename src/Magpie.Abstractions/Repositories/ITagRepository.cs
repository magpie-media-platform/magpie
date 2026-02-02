using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Read-only repository for querying tags.
/// </summary>
public interface ITagRepository : IRepository<Tag>
{
    /// <summary>
    /// Get all tags that start with the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<Tag>> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all tags associated with an entity by its ID.
    /// </summary>
    /// <remarks>
    /// This method searches across all tag relationships without requiring knowledge of the entity type.
    /// </remarks>
    /// <param name="entityId">The entity ID to find tags for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<Tag>> GetTagsByEntityAsync(Guid entityId, CancellationToken cancellationToken = default);
}
