using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Import;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Content;

internal sealed class LibraryImportService(
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IContentService contentService,
    IContentFileService contentFileService,
    IContentTypeRegistry contentTypeRegistry,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IRootFolderRepository rootFolderRepository,
    IFileSystem fileSystem,
    ITransactionManager transactionManager,
    IAuditRecordRepository auditRecordRepository,
    IJobManager jobManager,
    IEventBus eventBus,
    IReleaseEvaluationService releaseEvaluationService,
    TimeProvider timeProvider,
    ILogger<LibraryImportService> logger) : ILibraryImportService
{
    public async Task<PagedList<LibraryImportCandidate>> QueryCandidatesAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        return await libraryImportCandidateRepository.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LibraryImportSubmissionResult> SubmitImportAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var id in action.Ids ?? [])
        {
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        if (action.Filter is not null)
        {
            const int pageSize = 500;
            var pageNumber = 1;
            while (true)
            {
                var page = await libraryImportCandidateRepository.QueryAsync(
                    new()
                    {
                        RootFolderId = action.Filter.RootFolderId,
                        Status = LibraryImportCandidateStatus.Pending,
                        HasMatches = true,
                        ExcludeIds = action.Filter.ExcludeIds
                    },
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    new()
                    {
                        PropertyName = nameof(LibraryImportCandidate.Id),
                        Direction = SortDirection.Ascending
                    },
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                foreach (var candidate in pageItems)
                {
                    if (seen.Add(candidate.Id))
                    {
                        ids.Add(candidate.Id);
                    }
                }

                if (pageItems.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }
        }

        var queued = new List<LibraryImportQueuedResult>();
        var skipped = new List<LibraryImportSkippedResult>();

        foreach (var id in ids)
        {
            var claimed = await libraryImportCandidateRepository
                .TryClaimForImportAsync(id, actingUserId, CancellationToken.None)
                .ConfigureAwait(false);
            if (!claimed)
            {
                skipped.Add(new()
                {
                    CandidateId = id,
                    Reason = "Candidate is not pending, or was already claimed by another request."
                });
                continue;
            }

            try
            {
                var executionId = await jobManager.ExecuteAsync(
                    new JobEnqueueRequest
                    {
                        JobType = MagpieJobs.Types.RootFolderContentImportItem,
                        Parameters = new Dictionary<string, string>
                        {
                            [MagpieJobs.Parameters.CandidateId] = id.ToString("D")
                        },
                        Mode = JobExecutionMode.Queue,
                        UserId = actingUserId,
                        CorrelationId = correlationId,
                    },
                    CancellationToken.None).ConfigureAwait(false);

                queued.Add(new() { CandidateId = id, JobExecutionId = executionId });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The library import service failed to enqueue the import job for claimed library import " +
                    "candidate {LibraryImportCandidateId}",
                    id);
                await libraryImportCandidateRepository
                    .MarkImportFailedAsync(
                        id,
                        $"Failed to enqueue import job: {ex.Message}",
                        actingUserId,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                skipped.Add(new() { CandidateId = id, Reason = "Failed to enqueue import job." });
            }
        }

        logger.LogInformation(
            "The library import submission queued {Queued} candidates. The submission skipped or " +
            "failed {Skipped} of {Total} candidates",
            queued.Count,
            skipped.Count,
            ids.Count);

        return new() { Queued = queued, Skipped = skipped };
    }

    public async Task<LibraryImportCompletionResult> ImportCandidateAsync(
        Guid candidateId,
        int matchIndex,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await libraryImportCandidateRepository
            .GetByIdAsync(candidateId, cancellationToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            return Skipped("Library import candidate no longer exists; treating as already completed.");
        }

        if (candidate.Status == LibraryImportCandidateStatus.Queued)
        {
            var began = await libraryImportCandidateRepository
                .TryBeginImportAsync(candidateId, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            candidate = began
                ? candidate with { Status = LibraryImportCandidateStatus.Importing }
                : await libraryImportCandidateRepository
                    .GetByIdAsync(candidateId, cancellationToken)
                    .ConfigureAwait(false);
            if (candidate is null)
            {
                return Skipped("Library import candidate no longer exists; treating as already completed.");
            }
        }

        if (candidate.Status != LibraryImportCandidateStatus.Importing)
        {
            return Skipped($"Library import candidate is in status {candidate.Status} and cannot be imported.");
        }

        if (matchIndex < 0 || matchIndex >= candidate.Matches.Count)
        {
            var reason = $"matchIndex {matchIndex} is out of range; the candidate has " +
                $"{candidate.Matches.Count} match(es).";
            await libraryImportCandidateRepository
                .MarkImportFailedAsync(candidateId, reason, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            return Failed(reason);
        }

        var match = candidate.Matches[matchIndex];

        (Guid HierarchyItemId, Guid ContentItemId)? completion;
        try
        {
            completion = await CompleteImportAsync(candidate, match, actingUserId, correlationId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = $"Import failed: {ex.Message}";
            logger.LogWarning(
                ex,
                "The import failed for library import candidate {LibraryImportCandidateId} ({FilePath})",
                candidateId,
                candidate.FilePath);
            await libraryImportCandidateRepository
                .MarkImportFailedAsync(candidateId, reason, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            return Failed(reason);
        }

        if (completion is not { } result)
        {
            var reason = $"The provider found no match for {match.ExternalIdKey}={match.ExternalId}. " +
                "No content was created.";
            await libraryImportCandidateRepository
                .MarkImportFailedAsync(candidateId, reason, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            return Failed(reason);
        }

        await libraryImportCandidateRepository
            .DeleteAsync(candidate.Id, actingUserId, cancellationToken)
            .ConfigureAwait(false);

        await eventBus.PublishAsync(
            ContentEventBuilder.BuildContentImportedEvent(
                new()
                {
                    HierarchyItemId = result.HierarchyItemId,
                    ItemId = result.ContentItemId
                },
                actingUserId,
                1),
            cancellationToken).ConfigureAwait(false);

        return new()
        {
            Status = LibraryImportCompletionStatus.Completed,
            ContentHierarchyItemId = result.HierarchyItemId
        };
    }

    public async Task<LibraryImportCandidate?> MatchAsync(
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalIdKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var candidate = await libraryImportCandidateRepository
            .GetByIdAsync(candidateId, cancellationToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            return null;
        }

        var match = new LibraryImportMatch
        {
            Confidence = 1.0,
            ProviderType = providerType,
            ExternalIdKey = externalIdKey,
            ExternalId = externalId,
            Title = title,
            Subtitle = subtitle,
            ImageUrl = imageUrl,
            LevelSlug = levelSlug
        };

        var updated = candidate with
        {
            Matches = [match],
            MatchCount = 1,
            ModifiedAt = timeProvider.GetUtcNow().UtcDateTime,
            ModifiedByUserId = actingUserId
        };
        await libraryImportCandidateRepository.UpdateAsync(updated, actingUserId, cancellationToken)
            .ConfigureAwait(false);

        await TryWriteAuditAsync(
            operation: "FileMatched",
            entityType: nameof(LibraryImportCandidate),
            entityId: candidate.Id.ToString(),
            detail: $"Manually matched {Path.GetFileName(candidate.FilePath)} to " +
                $"{match.ExternalIdKey}={match.ExternalId}.",
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        return updated;
    }

    public async Task<bool> UpdateFulfillmentFromFilesAsync(
        Guid contentHierarchyItemId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await contentFileService
            .UpdateFulfillmentAsync(contentHierarchyItemId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveFileLeafAsync(
        Guid containerHierarchyItemId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var assignments = await AssignFilesToLeavesAsync(containerHierarchyItemId, [filePath], cancellationToken)
            .ConfigureAwait(false);
        if (assignments.Count == 0)
        {
            logger.LogDebug(
                "The content service resolved no leaf for file {FilePath} under container " +
                "{ContainerContentHierarchyItemId}. The file stays on the container",
                filePath,
                containerHierarchyItemId);
            return null;
        }

        logger.LogInformation(
            "The content service matched file {FilePath} to leaf hierarchy item {LeafContentHierarchyItemId} " +
            "under container {ContainerContentHierarchyItemId}",
            filePath,
            assignments[0].LeafId,
            containerHierarchyItemId);
        return assignments[0].LeafId;
    }

    public async Task<bool> DeleteMediaFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var operation = await contentFileService
            .DeleteAsync(contentHierarchyItemId, mediaFileId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
        if (operation.FulfillmentChanged)
        {
            await contentService.MaterializeFulfillmentStatusAsync(
                contentHierarchyItemId,
                actingUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return operation.Deleted;
    }

    private IReadOnlyList<string> ResolveTerminalLevelSlugs(ContentTypeDefinition definition)
    {
        var parentSlugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in definition.LevelDefinitions)
        {
            if (level.ParentSlug is { } parentSlug)
            {
                parentSlugs.Add(parentSlug);
            }
        }

        return
        [.. definition.LevelDefinitions.Select(level => level.Slug).Where(slug => !parentSlugs.Contains(slug))];
    }

    private async Task<IReadOnlyList<(string FilePath, Guid LeafId)>> AssignFilesToLeavesAsync(
        Guid containerHierarchyItemId,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        if (filePaths.Count == 0)
        {
            return [];
        }

        var container = await contentHierarchyItemRepository
            .GetByIdAsync(containerHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (container is null ||
            contentTypeRegistry.GetDefinitionFor(container.ContentType) is not { } definition)
        {
            return [];
        }

        var leafLevelSlugs = ResolveTerminalLevelSlugs(definition)
            .Where(slug => !string.Equals(slug, container.Level, StringComparison.Ordinal))
            .ToArray();
        if (leafLevelSlugs.Length == 0)
        {
            return [];
        }

        const int pageSize = 500;
        var leaves = new List<ContentHierarchyItem>();
        var pageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                new ContentHierarchyItemCriteria
                {
                    AncestorId = containerHierarchyItemId,
                    LeafOnly = true,
                    Level = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And =
                        [
                            new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.In,
                                Values = leafLevelSlugs,
                            },
                        ],
                    },
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            leaves.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        if (leaves.Count == 0)
        {
            return [];
        }

        var release = new Release { Protocol = "file", Title = Path.GetFileName(filePaths[0]) };
        IReadOnlyList<ReleaseFileRef> files =
            [.. filePaths.Select(path => new ReleaseFileRef(path, Path.GetFileName(path)))];
        var assignments = await releaseEvaluationService
            .AssignFilesToLeavesAsync(release, container, leaves, files, cancellationToken)
            .ConfigureAwait(false);

        var leafIds = leaves.Select(leaf => leaf.Id).ToHashSet();
        return
        [..
            from assignment in assignments
            where assignment.LeafContentHierarchyItemId is { } leafId && leafIds.Contains(leafId)
            select (assignment.FileKey, assignment.LeafContentHierarchyItemId!.Value)];
    }

    private LibraryImportCompletionResult Skipped(string message) =>
        new() { Status = LibraryImportCompletionStatus.Skipped, Message = message };

    private LibraryImportCompletionResult Failed(string message) =>
        new() { Status = LibraryImportCompletionStatus.Failed, Message = message };

    private async Task<(Guid HierarchyItemId, Guid ContentItemId)?> CompleteImportAsync(
        LibraryImportCandidate candidate,
        LibraryImportMatch match,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var hydrated = await contentService.HydrateFromExternalIdAsync(
            match.ProviderType,
            match.ExternalIdKey,
            match.ExternalId,
            candidate.ContentType,
            match.LevelSlug,
            candidate.RootFolderId,
            monitoring: null,
            qualityTierListId: null,
            requestedViaWishlistId: null,
            tags: null,
            actingUserId,
            cancellationToken).ConfigureAwait(false);

        if (hydrated is null)
        {
            return null;
        }

        var containerHierarchyItemId = hydrated.Id;
        var hierarchyItemId = containerHierarchyItemId;
        var contentItemId = Guid.Empty;
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var leafHierarchyItemId = await ResolveFileLeafAsync(
                    containerHierarchyItemId,
                    candidate.FilePath,
                    ct)
                    .ConfigureAwait(false);
                hierarchyItemId = leafHierarchyItemId ?? containerHierarchyItemId;

                var discoveredPath = await ResolveDiscoveredPathAsync(
                    hierarchyItemId,
                    candidate.FilePath,
                    ct).ConfigureAwait(false);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                if (discoveredPath is not null)
                {
                    var pathOwner = await contentHierarchyItemRepository
                        .GetByIdAsync(discoveredPath.ContentHierarchyItemId, ct)
                        .ConfigureAwait(false);
                    if (pathOwner is null)
                    {
                        throw new KeyNotFoundException(
                            $"Content hierarchy item {discoveredPath.ContentHierarchyItemId} no longer exists.");
                    }

                    var pathOrigin = pathOwner.Settings.GetValueOrDefault("content.path.origin");
                    var canReplacePath = string.Equals(pathOrigin, "Generated", StringComparison.OrdinalIgnoreCase) ||
                        pathOrigin is null && pathOwner.Path is null;
                    if (!canReplacePath ||
                        !string.Equals(pathOwner.Path, discoveredPath.OriginalPath, StringComparison.Ordinal) ||
                        pathOwner.ModifiedAt != discoveredPath.OriginalModifiedAt)
                    {
                        throw new InvalidOperationException(
                            $"The path of content hierarchy item {discoveredPath.ContentHierarchyItemId} " +
                            "changed during discovery.");
                    }

                    pathOwner.Path = discoveredPath.AbsolutePath;
                    pathOwner.Settings["content.path.origin"] = "Generated";
                    var pathUpdated = await contentHierarchyItemRepository
                        .UpdateAsync(pathOwner, actingUserId, ct)
                        .ConfigureAwait(false);
                    if (!pathUpdated)
                    {
                        throw new KeyNotFoundException(
                            $"Content hierarchy item {discoveredPath.ContentHierarchyItemId} no longer exists.");
                    }
                }

                var attachment = await contentFileService
                    .AttachImportedAsync(hierarchyItemId, candidate.FilePath, actingUserId, ct)
                    .ConfigureAwait(false);
                contentItemId = attachment.Result?.MediaFile.ContentItemId
                    ?? throw new KeyNotFoundException(
                        $"Content hierarchy item {hierarchyItemId} no longer exists.");

                if (attachment.FulfillmentChanged)
                {
                    await contentService.MaterializeFulfillmentStatusAsync(
                        hierarchyItemId,
                        actingUserId,
                        cancellationToken: ct).ConfigureAwait(false);
                }

                await auditRecordRepository.CreateAsync(
                    new AuditRecord
                    {
                        Category = AuditCategories.Jobs,
                        Operation = "FileImported",
                        EntityType = nameof(ContentHierarchyItem),
                        EntityId = hierarchyItemId.ToString(),
                        Detail = $"Imported {Path.GetFileName(candidate.FilePath)} using external ID " +
                            $"{match.ExternalIdKey}={match.ExternalId}.",
                        Timestamp = now,
                        UserId = actingUserId,
                        CorrelationId = correlationId
                    },
                    ct).ConfigureAwait(false);
            },
        cancellationToken).ConfigureAwait(false);

        return (hierarchyItemId, contentItemId);
    }

    private async Task<DiscoveredPath?> ResolveDiscoveredPathAsync(
        Guid contentHierarchyItemId,
        string filePath,
        CancellationToken cancellationToken)
    {
        var item = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var rootFolder = lineage.Count == 0
            ? null
            : await rootFolderRepository
                .GetByIdAsync(lineage[0].EffectiveRootFolderId, cancellationToken)
                .ConfigureAwait(false);
        string? effectivePath = null;
        if (rootFolder is not null && lineage.Count > 0)
        {
            var root = lineage[^1];
            var subfolder = contentTypeRegistry.GetDefinitionFor(root.ContentType)?.LevelDefinitions
                .FirstOrDefault(level => level.Slug == root.Level)?.Subfolder;
            effectivePath = fileSystem.GetFullPath(string.IsNullOrWhiteSpace(subfolder)
                ? rootFolder.Path.Trim()
                : Path.Combine(rootFolder.Path.Trim(), subfolder));
            for (var index = lineage.Count - 1; index >= 0; index--)
            {
                var itemPath = lineage[index].Path;
                if (itemPath is null)
                {
                    continue;
                }

                var normalized = itemPath.Trim().Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                effectivePath = Path.IsPathFullyQualified(normalized)
                    ? fileSystem.GetFullPath(normalized)
                    : fileSystem.GetFullPath(Path.Combine(effectivePath, normalized));
            }
        }

        var fileDirectory = Path.GetDirectoryName(fileSystem.GetFullPath(filePath));
        if (item is null ||
            effectivePath is null ||
            string.IsNullOrEmpty(fileDirectory) ||
            fileSystem.PathsEqual(effectivePath, fileDirectory))
        {
            return null;
        }

        var leaf = lineage.FirstOrDefault();
        var leafOwnsPath = leaf is not null &&
            contentTypeRegistry.GetDefinitionFor(leaf.ContentType)?.LevelDefinitions
                .FirstOrDefault(level => level.Slug == leaf.Level)?.Subfolder is not null;
        var owner = leafOwnsPath ? leaf : lineage.Skip(1).FirstOrDefault() ?? leaf;
        var pathOrigin = owner?.Settings.GetValueOrDefault("content.path.origin");
        if (owner is null || !(
            string.Equals(pathOrigin, "Generated", StringComparison.OrdinalIgnoreCase) ||
            pathOrigin is null &&
            owner.Path is null))
        {
            return null;
        }

        return new DiscoveredPath(owner.Id, owner.Path, owner.ModifiedAt, fileDirectory);
    }

    private async Task TryWriteAuditAsync(
        string operation,
        string? entityType,
        string? entityId,
        string detail,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditRecordRepository.CreateAsync(
                new AuditRecord
                {
                    Category = AuditCategories.Jobs,
                    Operation = operation,
                    EntityType = entityType,
                    EntityId = entityId,
                    Detail = detail,
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    UserId = userId,
                    CorrelationId = correlationId
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The library import failed to write the {AuditOperation} audit row for entity " +
                "{EntityType}/{EntityId}",
                operation,
                entityType,
                entityId);
        }
    }

    private sealed record DiscoveredPath(
        Guid ContentHierarchyItemId,
        string? OriginalPath,
        DateTime? OriginalModifiedAt,
        string AbsolutePath);
}
