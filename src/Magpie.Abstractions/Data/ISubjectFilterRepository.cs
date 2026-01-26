using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Filters;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

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
    Task<Result<SubjectFilter?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all subject filters that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<SubjectFilter>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default);
}
