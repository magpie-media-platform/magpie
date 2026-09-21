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
using Magpie.Models.Patch;
using Magpie.Sdk.Content;
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
    IFileSystem fileSystem,
    ITransactionManager transactionManager,
    IAuditRecordRepository auditRecordRepository,
    IJobManager jobManager,
    IEventBus eventBus,
    IReleaseEvaluationService releaseEvaluationService,
    IEnumerable<IContentHierarchyMetadataProvider> metadataProviders,
    TimeProvider timeProvider,
    ILogger<LibraryImportService> logger) : ILibraryImportService
{
    private static string ExceptionMessages(Exception exception) =>
        string.Join(" -> ", GenerateExceptionMessages(exception));

    private static IEnumerable<string> GenerateExceptionMessages(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    public async Task<PagedList<LibraryImportCandidate>> QueryCandidatesAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        return await libraryImportCandidateRepository.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PagedList<LibraryImportCandidateGroup>> QueryCandidateGroupsAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        CancellationToken cancellationToken = default)
    {
        var page = await libraryImportCandidateRepository.QueryGroupsAsync(query, paging, cancellationToken)
            .ConfigureAwait(false);

        return new PagedList<LibraryImportCandidateGroup>
        {
            Items = [.. page.Items.Select(summary => BuildGroup(
                summary.GroupKey,
                summary.Representative,
                summary.StatusCounts))],
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            TotalItems = page.TotalItems
        };
    }

    private static string? ResolveRepresentativeTitle(LibraryImportCandidate candidate)
    {
        return FirstNonEmptySignal(candidate.ParsedSignals, ParsedFileSignalKeys.RootTitle)
            ?? FirstNonEmptySignal(candidate.ParsedSignals, ParsedFileSignalKeys.TitleToken)
            ?? FirstNonEmptySignal(candidate.ParsedSignals, ParsedFileSignalKeys.Title)
            ?? Path.GetFileNameWithoutExtension(candidate.FilePath);
    }

    private static string? FirstNonEmptySignal(Dictionary<string, List<string>> signals, string key) =>
        signals.TryGetValue(key, out var values)
            ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            : null;

    private async Task<IReadOnlyList<LibraryImportCandidate>> LoadAllCandidatesAsync(
        LibraryImportCandidateQuery query,
        CancellationToken cancellationToken)
    {
        return await libraryImportCandidateRepository.QueryAllAsync(
            query,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(
        LibraryImportCandidateQuery query,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var ids = new List<Guid>();
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await libraryImportCandidateRepository.QueryIdsAsync(
                query,
                new() { PageNumber = pageNumber, PageSize = pageSize },
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            ids.AddRange(pageItems);
            if (pageNumber * pageSize >= page.TotalItems || pageItems.Count == 0)
            {
                return ids;
            }
        }
    }

    private LibraryImportCandidateGroup BuildGroup(
        string groupKey,
        LibraryImportCandidate representative,
        IReadOnlyDictionary<LibraryImportCandidateStatus, int> statusCounts)
    {
        var match = representative.Matches.Count == 1 ? representative.Matches[0] : null;
        var title = representative.LayoutKey is { } layoutKey
            ? Path.GetFileName(layoutKey)
            : match?.Title ?? ResolveRepresentativeTitle(representative) ?? groupKey;
        var (topLevelSlug, leafLevelSlug, leafLevelDisplayName, childLevelSlug, childLevelDisplayName) =
            ResolveLeafLevel(representative.ContentType, match?.LevelSlug);

        return new LibraryImportCandidateGroup
        {
            GroupKey = groupKey,
            Title = title,
            ContentType = representative.ContentType,
            Subtitle = match?.Subtitle,
            Match = match,
            LeafCount = statusCounts.Values.Sum(),
            TopLevelSlug = topLevelSlug,
            LeafLevelSlug = leafLevelSlug,
            LeafLevelDisplayName = leafLevelDisplayName,
            ChildLevelSlug = childLevelSlug,
            ChildLevelDisplayName = childLevelDisplayName,
            StatusCounts = statusCounts
        };
    }

    private (
        string TopLevelSlug,
        string LeafLevelSlug,
        string LeafLevelDisplayName,
        string? ChildLevelSlug,
        string? ChildLevelDisplayName) ResolveLeafLevel(string contentType, string? knownLevelSlug)
    {
        var topSlug = knownLevelSlug ?? contentTypeRegistry.GetRootLevels(contentType).FirstOrDefault()?.Slug
            ?? contentType;

        var topChildren = contentTypeRegistry.GetChildLevels(contentType, topSlug);
        var childSlug = topChildren.Count == 1 ? topChildren[0].Slug : null;

        var leafSlug = topSlug;
        while (true)
        {
            var children = contentTypeRegistry.GetChildLevels(contentType, leafSlug);
            if (children.Count != 1)
            {
                break;
            }

            leafSlug = children[0].Slug;
        }

        var levelDefinitions = contentTypeRegistry.GetDefinitionFor(contentType)?.LevelDefinitions;
        var leafDisplayName = levelDefinitions?.FirstOrDefault(level => level.Slug == leafSlug)?.DisplayName
            ?? leafSlug;
        var childDisplayName = childSlug is null
            ? null
            : levelDefinitions?.FirstOrDefault(level => level.Slug == childSlug)?.DisplayName ?? childSlug;

        return (topSlug, leafSlug, leafDisplayName, childSlug, childDisplayName);
    }

    public async Task<LibraryImportSubmissionResult> SubmitImportAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        LibraryImportMonitoringMode monitoringMode,
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
            var claimable = action.Filter with
            {
                Status = null,
                Statuses = [LibraryImportCandidateStatus.Pending, LibraryImportCandidateStatus.Failed],
                HasMatches = true
            };
            foreach (var candidateId in await LoadCandidateIdsAsync(claimable, cancellationToken)
                .ConfigureAwait(false))
            {
                if (seen.Add(candidateId))
                {
                    ids.Add(candidateId);
                }
            }
        }

        var queued = new List<LibraryImportQueuedResult>();
        var skipped = new List<LibraryImportSkippedResult>();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = await libraryImportCandidateRepository
                .GetByIdAsync(id, cancellationToken)
                .ConfigureAwait(false);
            if (candidate is null || candidate.Matches.Count == 0)
            {
                skipped.Add(new()
                {
                    CandidateId = id,
                    Reason = candidate is null
                        ? "Candidate no longer exists."
                        : "Candidate has no match. Match it before you import it."
                });
                continue;
            }

            var claimed = await libraryImportCandidateRepository
                .TryClaimForImportAsync(id, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            if (!claimed)
            {
                skipped.Add(new()
                {
                    CandidateId = id,
                    Reason = "Candidate is not pending or failed, or was already claimed by another request."
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
                            [MagpieJobs.Parameters.CandidateId] = id.ToString("D"),
                            [MagpieJobs.Parameters.MonitoringMode] = monitoringMode.ToString()
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
            "The library import submission queued {Queued} candidates and skipped or failed {Skipped} " +
            "of {Total}",
            queued.Count,
            skipped.Count,
            ids.Count);

        return new() { Queued = queued, Skipped = skipped };
    }

    public async Task<LibraryImportSubmissionResult?> AcceptGroupAsync(
        LibraryImportCandidateQuery query,
        string groupKey,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        var members = await LoadCandidateIdsAsync(query with { GroupKey = groupKey }, cancellationToken)
            .ConfigureAwait(false);
        if (members.Count == 0)
        {
            return null;
        }

        return await SubmitImportAsync(
            new BulkAction<LibraryImportCandidateQuery> { Ids = members },
            monitoringMode,
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryImportSubmissionResult> AcceptGroupsAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        return await SubmitImportAsync(
            action,
            monitoringMode,
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryImportCandidateGroup?> MatchGroupAsync(
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
        CancellationToken cancellationToken = default)
    {
        var members = await LoadAllCandidatesAsync(query with { GroupKey = groupKey }, cancellationToken)
            .ConfigureAwait(false);
        if (members.Count == 0)
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
        var patched = await libraryImportCandidateRepository.BulkPatchAsync(
            new BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>
            {
                Ids = [.. members.Select(member => member.Id)],
                Patch = new() { Matches = new() { Value = [match] } }
            },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
        if (patched.Count == 0)
        {
            return null;
        }

        await TryWriteAuditAsync(
            operation: "FileMatched",
            entityType: nameof(LibraryImportCandidate),
            entityId: groupKey,
            detail: $"Manually matched {patched.Count} files of group {groupKey} to " +
                $"{match.ExternalIdKey}={match.ExternalId}.",
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        var representative = await libraryImportCandidateRepository
            .GetByIdAsync(members[0].Id, cancellationToken)
            .ConfigureAwait(false);
        if (representative is null)
        {
            return null;
        }

        return BuildGroup(
            representative.GroupKey ?? groupKey,
            representative,
            members
                .GroupBy(member => member.Status)
                .ToDictionary(group => group.Key, group => group.Count()));
    }

    public async Task<LibraryImportCompletionResult> ImportCandidateAsync(
        Guid candidateId,
        int matchIndex,
        LibraryImportMonitoringMode monitoringMode,
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
            completion = await CompleteImportAsync(
                candidate,
                match,
                monitoringMode,
                actingUserId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = $"Import failed: {ExceptionMessages(ex)}";
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
            var providerAnswered = metadataProviders.Any(provider =>
                provider.ProviderType == match.ProviderType &&
                provider.SupportedContentTypes.Any(
                    supported => supported == "*" || supported == candidate.ContentType));
            var reason = providerAnswered
                ? $"The provider found no match for {match.ExternalIdKey}={match.ExternalId}. " +
                    "The match was removed. Match the file again before you import it."
                : $"No {match.ProviderType} metadata provider is available for content type " +
                    $"{candidate.ContentType}. The match was kept. Import the file again when the provider " +
                    "is available.";
            var failed = await libraryImportCandidateRepository
                .MarkImportFailedAsync(candidateId, reason, actingUserId, cancellationToken)
                .ConfigureAwait(false);

            if (failed && providerAnswered)
            {
                await libraryImportCandidateRepository
                    .BulkPatchAsync(
                        new BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>
                        {
                            Ids = [candidateId],
                            Patch = new() { Matches = new() { Value = [] } }
                        },
                        actingUserId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

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

        var patched = await libraryImportCandidateRepository.BulkPatchAsync(
            new BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>
            {
                Ids = [candidate.Id],
                Patch = new() { Matches = new() { Value = [match] } }
            },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
        if (patched.Count == 0)
        {
            return null;
        }

        await TryWriteAuditAsync(
            operation: "FileMatched",
            entityType: nameof(LibraryImportCandidate),
            entityId: candidate.Id.ToString(),
            detail: $"Manually matched {Path.GetFileName(candidate.FilePath)} to " +
                $"{match.ExternalIdKey}={match.ExternalId}.",
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        return await libraryImportCandidateRepository
            .GetByIdAsync(candidate.Id, cancellationToken)
            .ConfigureAwait(false);
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

    private static ContentHierarchyItem? ResolveByTitleSignal(
        IReadOnlyList<ContentHierarchyItem> children,
        string? parsedTitle)
    {
        if (string.IsNullOrWhiteSpace(parsedTitle))
        {
            return null;
        }

        var matches = children
            .Where(child => string.Equals(child.Name.Trim(), parsedTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task<FileRouteResult> RouteFileAsync(
        ContentHierarchyItem root,
        LibraryImportCandidate candidate,
        CancellationToken cancellationToken)
    {
        var current = root;
        var parsedTitle = FirstNonEmptySignal(candidate.ParsedSignals, ParsedFileSignalKeys.Title);
        var release = new Release { Protocol = "file", Title = Path.GetFileName(candidate.FilePath) };
        var fileRef = new ReleaseFileRef(candidate.FilePath, Path.GetFileName(candidate.FilePath));

        while (true)
        {
            var children = await ListDirectChildrenAsync(current.Id, cancellationToken).ConfigureAwait(false);
            if (children.Count == 0)
            {
                var childLevels = contentTypeRegistry.GetChildLevels(current.ContentType, current.Level);
                if (childLevels.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"The target {string.Join(", ", childLevels.Select(level => level.Slug))} level does not " +
                        $"exist yet below {current.Level}.");
                }

                return new(current, StoppedEarly: false);
            }

            var next = await ResolveFileMatchAsync(release, current, children, fileRef, cancellationToken)
                    .ConfigureAwait(false)
                ?? ResolveByTitleSignal(children, parsedTitle);
            if (next is null)
            {
                return new(current, StoppedEarly: true);
            }

            current = next;
        }
    }

    private async Task<ContentHierarchyItem?> ResolveFileMatchAsync(
        Release release,
        ContentHierarchyItem parent,
        IReadOnlyList<ContentHierarchyItem> children,
        ReleaseFileRef fileRef,
        CancellationToken cancellationToken)
    {
        var assignments = await releaseEvaluationService
            .AssignFilesToLeavesAsync(release, parent, children, [fileRef], cancellationToken)
            .ConfigureAwait(false);
        var leafId = assignments.FirstOrDefault()?.LeafContentHierarchyItemId;
        return leafId is { } id ? children.FirstOrDefault(child => child.Id == id) : null;
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> ListDirectChildrenAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        return await contentHierarchyItemRepository.QueryAllAsync(
            new ContentHierarchyItemCriteria
            {
                ParentId = new CriteriaSet<PropertyCriterion<Guid>>
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = parentId }]
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private LibraryImportCompletionResult Skipped(string message) =>
        new() { Status = LibraryImportCompletionStatus.Skipped, Message = message };

    private LibraryImportCompletionResult Failed(string message) =>
        new() { Status = LibraryImportCompletionStatus.Failed, Message = message };

    private async Task<(Guid HierarchyItemId, Guid ContentItemId)?> CompleteImportAsync(
        LibraryImportCandidate candidate,
        LibraryImportMatch match,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var hydrated = await contentService.HydrateWithDescendantsFromExternalIdAsync(
            match.ProviderType,
            match.ExternalIdKey,
            match.ExternalId,
            candidate.ContentType,
            match.LevelSlug,
            candidate.RootFolderId,
            monitoring: monitoringMode switch
            {
                LibraryImportMonitoringMode.Monitored => true,
                LibraryImportMonitoringMode.Unmonitored => false,
                LibraryImportMonitoringMode.Future => false,
                _ => null,
            },
            qualityTierListId: null,
            requestedViaWishlistId: null,
            tags: null,
            actingUserId,
            cancellationToken).ConfigureAwait(false);

        if (hydrated is null)
        {
            return null;
        }

        await ApplyImportMonitoringAsync(
            hydrated.Value.Root,
            hydrated.Value.CreatedHierarchyItemIds,
            monitoringMode,
            actingUserId,
            cancellationToken)
            .ConfigureAwait(false);

        var root = hydrated.Value.Root;
        var routed = await RouteFileAsync(root, candidate, cancellationToken).ConfigureAwait(false);
        var hierarchyItemId = routed.Node.Id;
        var fallbackNote = routed.StoppedEarly
            ? $" The file could not be aligned below level {routed.Node.Level}; it was attached there."
            : string.Empty;

        var discoveredPath = await ResolveDiscoveredPathAsync(
            hierarchyItemId,
            candidate.FilePath,
            cancellationToken).ConfigureAwait(false);

        var contentItemId = await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
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

                    var pathUpdated = await contentHierarchyItemRepository
                        .PatchAsync(
                            new ContentHierarchyItemPatch
                            {
                                Id = pathOwner.Id,
                                Path = new() { Value = discoveredPath.AbsolutePath },
                                Settings = new()
                                {
                                    Add = new Dictionary<string, string>
                                    {
                                        ["content.path.origin"] = "Generated"
                                    }
                                }
                            },
                            actingUserId,
                            ct)
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
                var attachedContentItemId = attachment.Result?.MediaFile.ContentItemId
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
                            $"{match.ExternalIdKey}={match.ExternalId}.{fallbackNote}",
                        Timestamp = now,
                        UserId = actingUserId,
                        CorrelationId = correlationId
                    },
                    ct).ConfigureAwait(false);
                return attachedContentItemId;
            },
        cancellationToken).ConfigureAwait(false);

        return (hierarchyItemId, contentItemId);
    }

    private async Task ApplyImportMonitoringAsync(
        ContentHierarchyItem root,
        IReadOnlyCollection<Guid> createdHierarchyItemIds,
        LibraryImportMonitoringMode mode,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        if (createdHierarchyItemIds.Count == 0)
        {
            return;
        }

        if (mode != LibraryImportMonitoringMode.Future)
        {
            return;
        }

        if (createdHierarchyItemIds.Contains(root.Id))
        {
            await contentHierarchyItemRepository.BulkPatchAsync(
                new BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
                {
                    Ids = [root.Id],
                    Patch = new() { Monitoring = new ScalarPatch<bool?> { Value = true } },
                },
                actingUserId,
                cancellationToken).ConfigureAwait(false);
        }

        var leafLevelSlug = ResolveLeafLevel(root.ContentType, root.Level).LeafLevelSlug;
        var createdDescendantIds = createdHierarchyItemIds.Where(id => id != root.Id).ToArray();
        if (createdDescendantIds.Length > 0)
        {
            await contentHierarchyItemRepository.BulkPatchAsync(
                new BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
                {
                    Filter = new()
                    {
                        Ids = createdDescendantIds,
                        Level = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.NotEquals, Value = leafLevelSlug }],
                        },
                    },
                    Patch = new() { Monitoring = new ScalarPatch<bool?> { Value = null } },
                },
                actingUserId,
                cancellationToken).ConfigureAwait(false);
        }

        await contentHierarchyItemRepository.BulkPatchAsync(
            new BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
            {
                Filter = new()
                {
                    Ids = [.. createdHierarchyItemIds],
                    Level = new()
                    {
                        And = [new() { Value = leafLevelSlug }],
                    },
                    Metadata = new()
                    {
                        And =
                        [
                            new MetadataCriteria
                            {
                                PublishDate = new()
                                {
                                    And =
                                    [
                                        new PropertyCriterion<TemporalValue>
                                        {
                                            Operator = CriteriaValueOperator.GreaterThan,
                                            Value = new()
                                            {
                                                Value = timeProvider.GetUtcNow().UtcDateTime,
                                                Precision = TemporalPrecision.Instant,
                                            },
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                },
                Patch = new() { Monitoring = new ScalarPatch<bool?> { Value = true } },
            },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
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

        var effectivePath = await contentFileService
            .GetEffectivePathAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var fileDirectory = Path.GetDirectoryName(fileSystem.GetFullPath(filePath));
        if (item is null ||
            string.IsNullOrEmpty(effectivePath) ||
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

    private sealed record FileRouteResult(ContentHierarchyItem Node, bool StoppedEarly);
}
