using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for subjects.
/// </summary>
public interface ISubjectService
{
    /// <summary>
    /// Queries the subjects.
    /// </summary>
    /// <param name="query">The filter criteria for the subjects</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The sort field and direction, or <see langword="null"/> for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<Subject>> QueryAsync(
        SubjectQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A subject by id.
    /// </summary>
    /// <param name="subjectId">The id of the subject to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Subject?> GetAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a subject.
    /// </summary>
    /// <param name="subjectId">The id of the subject to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid subjectId,
        SubjectPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a subject.
    /// </summary>
    /// <param name="subjectId">The id of the subject to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid subjectId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);
}
