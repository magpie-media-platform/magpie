using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for subjects.
/// </summary>
public interface ISubjectRepository :
    IQueryRepository<Subject, SubjectQuery>,
    ICrudRepository<Guid, Subject>,
    IPatchRepository<Guid, Subject, SubjectRowPatch>,
    IBulkDeleteRepository<Subject, SubjectQuery>
{
    /// <summary>
    /// Lists subjects of one kind that share a normalized external identifier with the supplied identities.
    /// </summary>
    /// <param name="kind">The subject kind to search</param>
    /// <param name="externalIds">The external identities to match without changing their stored values</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Guid>> ListIdsByExternalIdentityAsync(
        string kind,
        IReadOnlyCollection<ExternalId> externalIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the identity details of subjects.
    /// </summary>
    /// <param name="ids">The subject IDs to read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Subject>> ListIdentityDetailsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subject.
    /// </summary>
    /// <param name="model">The subject to create</param>
    /// <param name="createdByUserId">
    /// The id of the user that creates the subject, or <see langword="null"/> for the system
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the subject is new, otherwise <see langword="false"/></returns>
    Task<bool> TryCreateAsync(Subject model, Guid? createdByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds duplicate subjects.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>Each set of duplicate subjects with their external ids</returns>
    Task<IReadOnlyList<IReadOnlyList<Subject>>> FindDuplicatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the relationships, tags and contributions of duplicate subjects to one subject.
    /// </summary>
    /// <param name="survivorId">The id of the subject that stays</param>
    /// <param name="duplicateIds">The ids of the duplicate subjects</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task MergeAsync(
        Guid survivorId,
        IReadOnlyCollection<Guid> duplicateIds,
        CancellationToken cancellationToken = default);
}
