using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for library import candidates.
/// </summary>
public interface ILibraryImportCandidateRepository :
    ICrudRepository<Guid, LibraryImportCandidate>,
    IQueryRepository<LibraryImportCandidate, LibraryImportCandidateQuery>
{
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
    /// Claims a pending candidate for import and moves it to
    /// <see cref="LibraryImportCandidateStatus.Queued"/> in a single atomic step.
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
    /// Moves a queued candidate to
    /// <see cref="LibraryImportCandidateStatus.Importing"/>.
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
