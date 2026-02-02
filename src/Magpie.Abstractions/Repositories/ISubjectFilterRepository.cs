using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing subject filters.
/// </summary>
public interface ISubjectFilterRepository :
    ICrudRepository<Guid, SubjectFilter>,
    IPatchRepository<Guid, SubjectFilter, SubjectFilterPatch>
{
    /// <summary>
    /// Get a subject filter by name.
    /// </summary>
    /// <param name="name">The filter name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<SubjectFilter?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// List subject filters that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<SubjectFilter>> ListByTagAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all subject filters with a specific user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<SubjectFilter>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
