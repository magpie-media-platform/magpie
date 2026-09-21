using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for library import candidates.
/// </summary>
public interface ILibraryImportCandidateRepository :
    ICrudRepository<Guid, LibraryImportCandidate>,
    IQueryRepository<LibraryImportCandidate, LibraryImportCandidateQuery>,
    IBulkPatchRepository<Guid, LibraryImportCandidate, LibraryImportCandidateQuery, LibraryImportCandidatePatch>
{
    /// <summary>
    /// Queries the candidate groups, one page of groups at a time.
    /// </summary>
    /// <param name="query">The filter criteria for the candidates the groups are built from</param>
    /// <param name="paging">The page number and page size, applied to the groups</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>One page of group aggregates, ordered by group key</returns>
    Task<PagedList<LibraryImportCandidateGroupSummary>> QueryGroupsAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        CancellationToken cancellationToken);

    /// <summary>
    /// Queries one page of the ids of the candidates that match, without the rows of the candidates.
    /// </summary>
    /// <param name="query">The criteria that a candidate must match</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The ids of the candidates on the page, ordered by id, and the total across all pages</returns>
    Task<PagedList<Guid>> QueryIdsAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every import candidate of a root folder.
    /// </summary>
    /// <param name="rootFolderId">The id of the root folder that owns the candidates</param>
    /// <param name="deletedByUserId">
    /// The id of the user that deletes the records, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The number of records that the delete removed</returns>
    Task<int> DeleteByRootFolderAsync(
        Guid rootFolderId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims a pending or failed candidate and moves it to <see cref="LibraryImportCandidateStatus.Queued"/>.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="claimedByUserId">The id of the user that claims it, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when this call wins the claim</returns>
    Task<bool> TryClaimForImportAsync(
        Guid candidateId,
        Guid? claimedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a queued candidate to <see cref="LibraryImportCandidateStatus.Importing"/>.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="userId">The id of the user that starts the import, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the candidate moves</returns>
    Task<bool> TryBeginImportAsync(
        Guid candidateId,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a queued or importing candidate to the pending state.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="userId">The id of the user that started the import, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the candidate returns to the pending state</returns>
    Task<bool> TryResetImportAsync(
        Guid candidateId,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a candidate that waits for match resolution as failed.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="reason">The text that describes the failure</param>
    /// <param name="userId">The id of the user that starts the operation, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the candidate moves to the failed state</returns>
    Task<bool> MarkMatchingFailedAsync(
        Guid candidateId,
        string reason,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a queued or importing candidate as failed.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="reason">The text that describes the failure</param>
    /// <param name="userId">The id of the user that starts the operation, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the candidate moves to the failed state</returns>
    Task<bool> MarkImportFailedAsync(
        Guid candidateId,
        string reason,
        Guid? userId,
        CancellationToken cancellationToken);
}
