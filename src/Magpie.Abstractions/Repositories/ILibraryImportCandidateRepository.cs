using System;
using System.Collections.Generic;
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
    /// Saves automatic match resolution only while the candidate waits for matching.
    /// </summary>
    /// <param name="model">The resolved candidate</param>
    /// <param name="modifiedByUserId">The id of the user that starts resolution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>True when the resolution was saved</returns>
    Task<bool> TryResolveMatchAsync(
        LibraryImportCandidate model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reclaims an inactive import candidate.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="inactivityCutoff">The latest candidate activity time eligible for recovery</param>
    /// <param name="userId">The id of the user that starts recovery</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryReclaimInactiveImportAsync(
        Guid candidateId,
        DateTime inactivityCutoff,
        Guid? userId,
        CancellationToken cancellationToken);

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
    /// Deletes the import candidates of a root folder that no import owns and that the user did not reject.
    /// </summary>
    /// <param name="rootFolderId">The id of the root folder that owns the candidates</param>
    /// <param name="candidateIds">The ids of the candidates to delete, or <c>null</c> to delete all of them</param>
    /// <param name="deletedByUserId">
    /// The id of the user that deletes the records, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The number of records that the delete removed</returns>
    Task<int> DeleteByRootFolderAsync(
        Guid rootFolderId,
        IReadOnlyCollection<Guid>? candidateIds,
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
    /// <param name="note">The note that states why the candidate returns to the pending state, or <c>null</c></param>
    /// <param name="userId">The id of the user that started the import, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the candidate returns to the pending state</returns>
    Task<bool> TryResetImportAsync(
        Guid candidateId,
        string? note,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rejects a candidate that no import owns, and removes its matches and its library target.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="userId">The id of the user that rejects the candidate, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the candidate moves to <see cref="LibraryImportCandidateStatus.Rejected"/></returns>
    Task<bool> TryRejectAsync(
        Guid candidateId,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves the library target of an import candidate.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="contentHierarchyItemId">The id of the existing library item, or <c>null</c> when there is none</param>
    /// <param name="userId">The id of the user that starts the operation, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the target was saved</returns>
    Task<bool> TryUpdateTargetAsync(
        Guid candidateId,
        Guid? contentHierarchyItemId,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a rejected candidate to <see cref="LibraryImportCandidateStatus.MatchingPending"/>.
    /// </summary>
    /// <param name="candidateId">The id of the import candidate</param>
    /// <param name="userId">The id of the user that restores the candidate, or <c>null</c> when no user did</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the candidate moves</returns>
    Task<bool> TryRestoreAsync(
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
