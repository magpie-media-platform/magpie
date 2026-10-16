using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Sdk.Releases;

namespace Magpie.Abstractions.Releases;

/// <summary>
/// The service contract for release matching and scoring against content targets.
/// </summary>
public interface IReleaseEvaluationService
{
    /// <summary>
    /// Creates the context that matches and scores releases for a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to evaluate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ReleaseEvaluationContext> CreateEvaluationContextAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assesses a release against a context that is already resolved.
    /// </summary>
    /// <param name="release">The release to assess</param>
    /// <param name="releaseFacts">The facts that the parser read from the release</param>
    /// <param name="context">The context for the target content hierarchy item</param>
    ReleaseAssessment AssessMatch(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        ReleaseEvaluationContext context);

    /// <summary>
    /// Tells if a release is a permitted upgrade over the file that the content item already holds.
    /// </summary>
    /// <param name="assessment">The assessment of the release against the target content item</param>
    /// <param name="release">The release to test</param>
    /// <param name="context">The context for the target content hierarchy item</param>
    bool IsAcceptableUpgradeOverCurrentFile(
        ReleaseAssessment assessment,
        Release release,
        ReleaseEvaluationContext context);

    /// <summary>
    /// Gets the quality rank that ranking uses.
    /// </summary>
    /// <param name="assessment">The assessment that holds the matched rank</param>
    /// <param name="context">The context that holds the quality tier list and the rank of the current file</param>
    int EffectiveQualityRank(ReleaseAssessment assessment, ReleaseEvaluationContext context);

    /// <summary>
    /// Scores a release against the quality tier list of a content hierarchy item.
    /// </summary>
    /// <param name="release">The release to score</param>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item that owns the tier list</param>
    /// <param name="currentQualityOrdinal">The quality rank of the file on disk, or <see langword="null"/></param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ReleaseAssessment?> ScoreForContentAsync(
        Release release,
        Guid contentHierarchyItemId,
        int? currentQualityOrdinal = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scores a download for a content hierarchy item.
    /// </summary>
    /// <param name="download">The download to score</param>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item that owns the tier list</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The assessment, or <see langword="null"/> when no quality tier matches</returns>
    Task<ReleaseAssessment?> ScoreDownloadForContentAsync(
        Download download,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scores the current file for its content item.
    /// </summary>
    /// <param name="mediaFile">The current media file and its extracted information</param>
    /// <param name="contentHierarchyItemId">The hierarchy item that owns the quality tier list</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ReleaseAssessment?> ScoreFileForContentAsync(
        MediaFile mediaFile,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scores the primary files of a content item from their stored media information, without reading the disk.
    /// </summary>
    /// <param name="primaryFiles">The primary files of the content item</param>
    /// <param name="qualityTierListId">The id of the effective quality tier list of the content item, if any</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The quality status and the best matched rank, or nulls when no file matches a tier</returns>
    Task<(QualityStatus? Quality, int? Rank)> ScoreMediaQualityAsync(
        IEnumerable<MediaFile> primaryFiles,
        Guid? qualityTierListId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assesses a release against a content hierarchy item.
    /// </summary>
    /// <param name="release">The release to assess</param>
    /// <param name="releaseFacts">The facts that the parser read from the release</param>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to assess against</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ReleaseAssessment> AssessForContentAsync(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the hierarchy nodes that a release fulfills for a content hierarchy item.
    /// </summary>
    /// <param name="release">The release to evaluate</param>
    /// <param name="releaseFacts">The facts that the parser read from the release</param>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item that was searched</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ReleaseScopeResolution> ResolveScopeAsync(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns the files of a release to the leaf content items that match them.
    /// </summary>
    /// <param name="release">The release that holds the files</param>
    /// <param name="parent">The content item that is the parent of the candidate leaves</param>
    /// <param name="leaves">The leaf content items that can take a file</param>
    /// <param name="files">The files to assign</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ReleaseFileAssignment>> AssignFilesToLeavesAsync(
        Release release,
        ContentHierarchyItem parent,
        IReadOnlyList<ContentHierarchyItem> leaves,
        IReadOnlyList<ReleaseFileRef> files,
        CancellationToken cancellationToken = default);
}
