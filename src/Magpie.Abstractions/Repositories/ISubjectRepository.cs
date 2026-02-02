using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing subjects.
/// </summary>
public interface ISubjectRepository : ICrudRepository<Guid, Subject>, IPatchRepository<Guid, Subject, TagsPatch>
{
    /// <summary>
    /// Get all subjects of a specific kind.
    /// </summary>
    /// <param name="kind">The kind of subject.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<Subject>> GetByKindAsync(string kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all subjects that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<Subject>> GetByTagAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the last refreshed timestamp for a subject.
    /// </summary>
    /// <param name="id">The subject id.</param>
    /// <param name="dateLastRefreshed">The timestamp of the last refresh.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<bool> UpdateLastRefreshedAsync(
        Guid id,
        DateTime dateLastRefreshed,
        CancellationToken cancellationToken = default);
}
