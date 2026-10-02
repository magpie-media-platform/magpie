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
}
