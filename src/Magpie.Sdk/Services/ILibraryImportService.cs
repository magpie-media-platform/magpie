using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Paging;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for library import candidates.
/// </summary>
public interface ILibraryImportService
{
    /// <summary>
    /// Previews optional import renames without creating or associating library content.
    /// </summary>
    /// <param name="selection">The candidate selection</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Magpie.Models.Api.FileRenamePreview> PreviewRenamesAsync(
        Magpie.Models.Api.LibraryImportRenameSelection selection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a file and then applies its explicitly accepted rename preview.
    /// </summary>
    /// <param name="candidateId">The claimed import candidate</param>
    /// <param name="matchIndex">The selected metadata match</param>
    /// <param name="monitoringMode">The import monitoring choice</param>
    /// <param name="preview">The accepted rename entry</param>
    /// <param name="actingUserId">The user that starts the import</param>
    /// <param name="correlationId">The import correlation id</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportCompletionResult> ImportWithRenameAsync(
        Guid candidateId,
        int matchIndex,
        LibraryImportMonitoringMode monitoringMode,
        Magpie.Models.Api.FileRenamePreview preview,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the library import candidates.
    /// </summary>
    /// <param name="query">The filter criteria for the candidates</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The sort field and direction, or <see langword="null"/> for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<LibraryImportCandidate>> QueryCandidatesAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the library import candidates, collapsed into top-level groups.
    /// </summary>
    /// <param name="query">The filter criteria for the underlying candidates</param>
    /// <param name="paging">The page number and page size, applied to the groups</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<LibraryImportCandidateGroup>> QueryCandidateGroupsAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the candidates the action selects and enqueues one durable import job for each.
    /// </summary>
    /// <param name="action">The selection of candidates to act on</param>
    /// <param name="monitoringMode">The monitoring setup to apply during import</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportSubmissionResult> SubmitImportAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims every candidate of one candidate group and enqueues one durable import job for each.
    /// </summary>
    /// <param name="query">The filter criteria for the candidates the group is built from</param>
    /// <param name="groupKey">The key of the group to accept, as returned by <see cref="QueryCandidateGroupsAsync"/></param>
    /// <param name="monitoringMode">The monitoring setup to apply during import</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The submission result, or <see langword="null"/> when no group has the given key</returns>
    Task<LibraryImportSubmissionResult?> AcceptGroupAsync(
        LibraryImportCandidateQuery query,
        string groupKey,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the candidates the action selects and enqueues one durable import job for each.
    /// </summary>
    /// <param name="action">The selection of candidates and candidate groups to act on</param>
    /// <param name="monitoringMode">The monitoring setup to apply during import</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportSubmissionResult> AcceptGroupsAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies one match to every candidate of a candidate group.
    /// </summary>
    /// <param name="query">The filter criteria for the candidates the group is built from</param>
    /// <param name="groupKey">The key of the group to match, as returned by <see cref="QueryCandidateGroupsAsync"/></param>
    /// <param name="providerType">The slug of the metadata provider that found the match</param>
    /// <param name="externalIdKey">The source name of the external id, as in <see cref="ExternalId.Source"/></param>
    /// <param name="externalId">The external identifier of the matched item</param>
    /// <param name="title">The display title of the matched item</param>
    /// <param name="subtitle">The secondary title, or <see langword="null"/> when there is none</param>
    /// <param name="imageUrl">The image URL of the matched item, or <see langword="null"/></param>
    /// <param name="levelSlug">The content level of the matched item, or <see langword="null"/></param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The updated group, or <see langword="null"/> when no group has the given key</returns>
    Task<LibraryImportCandidateGroup?> MatchGroupAsync(
        LibraryImportCandidateQuery query,
        string groupKey,
        string providerType,
        string externalIdKey,
        string externalId,
        string title,
        string? subtitle,
        string? imageUrl,
        string? levelSlug,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a claimed candidate and records the outcome on it.
    /// </summary>
    /// <param name="candidateId">The id of the claimed candidate to import</param>
    /// <param name="matchIndex">The index of the match to import in the candidate match list</param>
    /// <param name="monitoringMode">The monitoring setup to apply during import</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportCompletionResult> ImportCandidateAsync(
        Guid candidateId,
        int matchIndex,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the selected match of a library import candidate.
    /// </summary>
    /// <param name="candidateId">The id of the candidate to match</param>
    /// <param name="providerType">The slug of the metadata provider that found the match</param>
    /// <param name="externalIdKey">The source name of the external id, as in <see cref="ExternalId.Source"/></param>
    /// <param name="externalId">The external identifier of the matched item</param>
    /// <param name="title">The display title of the matched item</param>
    /// <param name="subtitle">The secondary title, or <see langword="null"/> when there is none</param>
    /// <param name="imageUrl">The image URL of the matched item, or <see langword="null"/></param>
    /// <param name="levelSlug">The content level of the matched item, or <see langword="null"/></param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportCandidate?> MatchAsync(
        Guid candidateId,
        string providerType,
        string externalIdKey,
        string externalId,
        string title,
        string? subtitle,
        string? imageUrl,
        string? levelSlug,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the existing library item that the file of a candidate with one match belongs to, without creating content.
    /// </summary>
    /// <param name="candidate">The candidate with its match</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The id of the existing item, or <see langword="null"/> when the library has no item for the file</returns>
    Task<Guid?> ResolveLibraryTargetAsync(
        LibraryImportCandidate candidate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the candidates that target an existing library item or one of its descendants.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the library item</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The candidates that wait for association or that an association owns</returns>
    Task<IReadOnlyList<LibraryImportCandidate>> QueryLibraryTargetCandidatesAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Associates each candidate whose existing library item has no primary file, at most one candidate for each item.
    /// </summary>
    /// <param name="candidates">The candidates of root folders that accept confident matches without review</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportSubmissionResult> AssociateMissingFilesAsync(
        IReadOnlyCollection<LibraryImportCandidate> candidates,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the existing library item again for candidates with one match, and saves the target when it changed.
    /// </summary>
    /// <param name="candidates">The candidates to resolve</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The candidates whose saved target changed, with the new target</returns>
    Task<IReadOnlyList<LibraryImportCandidate>> RefreshLibraryTargetsAsync(
        IReadOnlyCollection<LibraryImportCandidate> candidates,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims candidates that target existing library items and enqueues one durable association job for each.
    /// </summary>
    /// <param name="candidateIds">The ids of the candidates to associate</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportSubmissionResult> AssociateAsync(
        IReadOnlyCollection<Guid> candidateIds,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches the file of a claimed candidate to existing library content without creating content.
    /// </summary>
    /// <param name="candidateId">The id of the claimed candidate</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<LibraryImportCompletionResult> AssociateCandidateAsync(
        Guid candidateId,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects candidates, so that rescans keep them unmatched until the user restores them.
    /// </summary>
    /// <param name="action">The selection of candidates to reject</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The number of candidates that the operation rejected</returns>
    Task<int> RejectAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores rejected candidates and queues match resolution for them.
    /// </summary>
    /// <param name="action">The selection of candidates to restore</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The number of candidates that the operation restored</returns>
    Task<int> RestoreAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles direct fulfillment against the accepted primary files of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the item whose files changed</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UpdateFulfillmentFromFilesAsync(
        Guid contentHierarchyItemId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a media file from a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the item that holds the file</param>
    /// <param name="mediaFileId">The id of the media file to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteMediaFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the cleanup of files attached to containers and of duplicate copies, without a change to the library
    /// or to storage.
    /// </summary>
    /// <param name="rootId">The root item to plan for, or <see langword="null"/> for the whole library</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MediaFileCleanupPlan> PlanAsync(Guid? rootId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the approved rows of a file cleanup plan that still match the current plan.
    /// </summary>
    /// <param name="request">The approved rows</param>
    /// <param name="actingUserId">The user who applies the rows</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<MediaFileCleanupRowResult>> ApplyAsync(
        MediaFileCleanupApplyRequest request,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);
}
