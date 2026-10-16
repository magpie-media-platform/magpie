using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Api;
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
    IRootFolderRepository rootFolderRepository,
    ILogger<LibraryImportService> logger) : ILibraryImportService
{
    private static readonly Regex FilesystemSubstitutePattern = new(
        @"[/\\:*?""<>|+\-\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ExtraDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Behind The Scenes",
        "Bonus Features",
        "Deleted Scenes",
        "Extras",
        "Featurettes",
        "Interviews",
        "Other",
        "Sample",
        "Samples",
        "Scenes",
        "Shorts",
        "Special Features",
        "Trailers"
    };

    private static readonly Regex ExtraFileNamePattern = new(
        "^trailer$|-(behindthescenes|deleted|featurette|interview|other|scene|short|trailer)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static string ExceptionMessages(Exception exception) =>
        string.Join(" -> ", GenerateExceptionMessages(exception));

    private static IEnumerable<string> GenerateExceptionMessages(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    private static bool IsExtra(string? itemFolder, string filePath) =>
        (itemFolder is not null &&
            (Path.GetDirectoryName(Path.GetRelativePath(itemFolder, filePath)) ?? string.Empty)
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(ExtraDirectoryNames.Contains)) ||
        ReleaseFiles.IsSample(filePath) ||
        ExtraFileNamePattern.IsMatch(Path.GetFileNameWithoutExtension(filePath));

    private static bool IsVideo(MediaFile file) => file.FileKind == nameof(FileKind.Video);

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

        return await SubmitCoreAsync(
            ids,
            monitoringMode,
            (action as LibraryImportSubmissionRequest)?.RenamePreview,
            associate: false,
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryImportSubmissionResult> AssociateMissingFilesAsync(
        IReadOnlyCollection<LibraryImportCandidate> candidates,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        var targeted = candidates
            .Where(candidate => candidate.Matches.Count == 1 && candidate.ContentHierarchyItemId is not null)
            .OrderBy(candidate => candidate.FilePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Id)
            .ToList();
        if (targeted.Count == 0)
        {
            return new() { Queued = [], Skipped = [] };
        }

        var inFlight = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery
            {
                ContentHierarchyItemIds = [.. targeted.Select(candidate => candidate.ContentHierarchyItemId!.Value)],
                Statuses = [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing]
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var claimedTargets = inFlight.Select(candidate => candidate.ContentHierarchyItemId!.Value).ToHashSet();
        var ids = new List<Guid>();
        foreach (var candidate in targeted)
        {
            var target = candidate.ContentHierarchyItemId!.Value;
            if (!claimedTargets.Contains(target) &&
                await HasNoPrimaryFileAsync(target, cancellationToken).ConfigureAwait(false))
            {
                claimedTargets.Add(target);
                ids.Add(candidate.Id);
            }
        }

        return ids.Count == 0
            ? new() { Queued = [], Skipped = [] }
            : await AssociateAsync(ids, actingUserId, correlationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryImportCandidate>> RefreshLibraryTargetsAsync(
        IReadOnlyCollection<LibraryImportCandidate> candidates,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var changed = new List<LibraryImportCandidate>();
        foreach (var candidate in candidates.Where(candidate => candidate.Matches.Count == 1))
        {
            var target = await ResolveLibraryTargetAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (target != candidate.ContentHierarchyItemId &&
                await libraryImportCandidateRepository
                    .TryUpdateTargetAsync(candidate.Id, target, actingUserId, cancellationToken)
                    .ConfigureAwait(false))
            {
                changed.Add(candidate with { ContentHierarchyItemId = target });
            }
        }

        return changed;
    }

    private async Task<bool> HasNoPrimaryFileAsync(Guid contentHierarchyItemId, CancellationToken cancellationToken) =>
        await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false) is { } item && HasNoPrimaryFile(item);

    private bool HasNoPrimaryFile(ContentHierarchyItem item) =>
        LevelOf(item) is { } level && !(item.ContentItem?.MediaFiles ?? [])
            .Concat(item.ContentItem?.CoveringMediaFiles ?? [])
            .Any(file => FileSystem.IsPrimaryFile(fileSystem, level, file));

    public async Task<LibraryImportSubmissionResult> AssociateAsync(
        IReadOnlyCollection<Guid> candidateIds,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        return await SubmitCoreAsync(
            [.. candidateIds.Distinct()],
            LibraryImportMonitoringMode.Inherit,
            renamePreview: null,
            associate: true,
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<LibraryImportSubmissionResult> SubmitCoreAsync(
        IReadOnlyList<Guid> ids,
        LibraryImportMonitoringMode monitoringMode,
        FileRenamePreview? renamePreview,
        bool associate,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var queued = new List<LibraryImportQueuedResult>();
        var skipped = new List<LibraryImportSkippedResult>();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = await libraryImportCandidateRepository
                .GetByIdAsync(id, cancellationToken)
                .ConfigureAwait(false);
            if (candidate is null || candidate.Matches.Count == 0 ||
                associate && candidate.ContentHierarchyItemId is null)
            {
                skipped.Add(new()
                {
                    CandidateId = id,
                    Reason = candidate is null
                        ? "Candidate no longer exists."
                        : candidate.Matches.Count == 0
                            ? "Candidate has no match."
                            : "Candidate has no existing library item to associate with."
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
                var parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.CandidateId] = id.ToString("D"),
                    [MagpieJobs.Parameters.MonitoringMode] = monitoringMode.ToString()
                };
                if (associate)
                {
                    parameters[MagpieJobs.Parameters.Associate] = bool.TrueString;
                }
                else if (renamePreview is not null && renamePreview.Files.Any(file => file.CandidateId == id))
                {
                    parameters["renamePreview"] = JsonSerializer.Serialize(new FileRenamePreview
                    {
                        HasMask = renamePreview.HasMask,
                        Files = [.. renamePreview.Files.Where(file => file.CandidateId == id)]
                    });
                }

                var executionId = await jobManager.ExecuteAsync(
                    new JobEnqueueRequest
                    {
                        JobType = MagpieJobs.Types.RootFolderContentImportItem,
                        Parameters = parameters,
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

        var groups = await QueryCandidateGroupsAsync(
            new LibraryImportCandidateQuery
            {
                RootFolderId = query.RootFolderId,
                ScanRunId = query.ScanRunId,
                GroupKey = representative.GroupKey ?? groupKey
            },
            new() { PageNumber = 1, PageSize = 1 },
            cancellationToken).ConfigureAwait(false);
        return groups.Items.FirstOrDefault();
    }

    public Task<LibraryImportCompletionResult> ImportCandidateAsync(
        Guid candidateId,
        int matchIndex,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ImportCoreAsync(
            candidateId,
            matchIndex,
            monitoringMode,
            associate: false,
            actingUserId,
            correlationId,
            cancellationToken);

    public Task<LibraryImportCompletionResult> AssociateCandidateAsync(
        Guid candidateId,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ImportCoreAsync(
            candidateId,
            0,
            LibraryImportMonitoringMode.Inherit,
            associate: true,
            actingUserId,
            correlationId,
            cancellationToken);

    private async Task<LibraryImportCompletionResult> ImportCoreAsync(
        Guid candidateId,
        int matchIndex,
        LibraryImportMonitoringMode monitoringMode,
        bool associate,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
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
                associate,
                actingUserId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ChildLevelPendingException ex)
        {
            return new()
            {
                Status = LibraryImportCompletionStatus.Waiting,
                Message = ex.Message,
                ContentHierarchyItemId = ex.ContentHierarchyItemId
            };
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
                    "The match was removed."
                : $"No {match.ProviderType} metadata provider is available for content type " +
                    $"{candidate.ContentType}. The match was kept.";
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

    public async Task<Guid?> ResolveLibraryTargetAsync(
        LibraryImportCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Matches.Count != 1)
        {
            return null;
        }

        try
        {
            var root = await FindLibraryRootAsync(candidate, candidate.Matches[0], cancellationToken)
                .ConfigureAwait(false);
            if (root is null)
            {
                return null;
            }

            var routed = await RouteFileAsync(root, candidate.FilePath, candidate.ParsedSignals, cancellationToken)
                .ConfigureAwait(false);
            return routed.StoppedEarly ? null : routed.Node.Id;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The library import found no existing library item for candidate {LibraryImportCandidateId}",
                candidate.Id);
            return null;
        }
    }

    public async Task<IReadOnlyList<LibraryImportCandidate>> QueryLibraryTargetCandidatesAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 1_000;
        var targetIds = new List<Guid> { contentHierarchyItemId };
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await contentHierarchyItemRepository.QueryIdsAsync(
                new ContentHierarchyItemCriteria { AncestorId = contentHierarchyItemId },
                new PagingParameter { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            targetIds.AddRange(page);
            if (page.Count < pageSize)
            {
                break;
            }
        }

        return await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery
            {
                ContentHierarchyItemIds = targetIds,
                Statuses =
                [
                    LibraryImportCandidateStatus.Pending,
                    LibraryImportCandidateStatus.Failed,
                    LibraryImportCandidateStatus.Queued,
                    LibraryImportCandidateStatus.Importing
                ],
                HasMatches = true
            },
            new SortParameter
            {
                PropertyName = nameof(LibraryImportCandidate.FilePath),
                Direction = SortDirection.Ascending
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RejectAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var rejected = 0;
        foreach (var candidateId in await CollectIdsAsync(action, cancellationToken).ConfigureAwait(false))
        {
            if (await TransitionWithAuditAsync(
                    candidateId,
                    libraryImportCandidateRepository.TryRejectAsync,
                    "FileRejected",
                    "Rejected the matches of",
                    actingUserId,
                    correlationId: null,
                    cancellationToken).ConfigureAwait(false))
            {
                rejected++;
            }
        }

        return rejected;
    }

    public async Task<int> RestoreAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        var restored = 0;
        var selection = action with
        {
            Filter = action.Filter is null
                ? null
                : action.Filter with { Status = LibraryImportCandidateStatus.Rejected }
        };
        foreach (var candidateId in await CollectIdsAsync(selection, cancellationToken).ConfigureAwait(false))
        {
            if (await TransitionWithAuditAsync(
                    candidateId,
                    libraryImportCandidateRepository.TryRestoreAsync,
                    "FileRestored",
                    "Restored matching for",
                    actingUserId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false))
            {
                restored++;
            }
        }

        if (restored > 0)
        {
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = MagpieJobs.Types.LibraryImportMatchResolution,
                    Mode = JobExecutionMode.Queue,
                    UserId = actingUserId,
                    CorrelationId = correlationId,
                },
                cancellationToken).ConfigureAwait(false);
        }

        return restored;
    }

    private async Task<bool> TransitionWithAuditAsync(
        Guid candidateId,
        Func<Guid, Guid?, CancellationToken, Task<bool>> transition,
        string operation,
        string detail,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var candidate = await libraryImportCandidateRepository.GetByIdAsync(candidateId, ct)
                    .ConfigureAwait(false);
                if (candidate is null || !await transition(candidateId, actingUserId, ct).ConfigureAwait(false))
                {
                    return false;
                }

                await auditRecordRepository.CreateAsync(
                    new AuditRecord
                    {
                        Category = AuditCategories.Jobs,
                        Operation = operation,
                        EntityType = nameof(LibraryImportCandidate),
                        EntityId = candidateId.ToString(),
                        Detail = $"{detail} {Path.GetFileName(candidate.FilePath)}.",
                        Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                        UserId = actingUserId,
                        CorrelationId = correlationId
                    },
                    ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyCollection<Guid>> CollectIdsAsync(
        BulkAction<LibraryImportCandidateQuery> action,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>(action.Ids ?? []);
        if (action.Filter is not null)
        {
            ids.UnionWith(await LoadCandidateIdsAsync(action.Filter, cancellationToken).ConfigureAwait(false));
        }

        return ids;
    }

    private async Task<ContentHierarchyItem?> FindLibraryRootAsync(
        LibraryImportCandidate candidate,
        LibraryImportMatch match,
        CancellationToken cancellationToken)
    {
        var level = contentTypeRegistry.GetRootLevelSlug(candidate.ContentType, match.LevelSlug);
        var found = await contentHierarchyItemRepository.FindByExternalIdentitiesAsync(
            [(candidate.ContentType, level, match.ExternalIdKey, match.ExternalId)],
            cancellationToken).ConfigureAwait(false);
        if (found.Count == 0)
        {
            return null;
        }

        var root = await contentHierarchyItemRepository.GetByIdAsync(found.Values.Single(), cancellationToken)
            .ConfigureAwait(false);
        return root?.EffectiveRootFolderId == candidate.RootFolderId ? root : null;
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

    public Task<MediaFileCleanupPlan> PlanAsync(Guid? rootId, CancellationToken cancellationToken = default) =>
        BuildPlanAsync(rootId, rankPlannedReassigns: true, cancellationToken);

    private async Task<MediaFileCleanupPlan> BuildPlanAsync(
        Guid? rootId,
        bool rankPlannedReassigns,
        CancellationToken cancellationToken)
    {
        var items = await LoadItemsWithFilesAsync(rootId, cancellationToken).ConfigureAwait(false);
        var context = new PlanContext { RankPlannedReassigns = rankPlannedReassigns };
        foreach (var item in items)
        {
            context.Items[item.Id] = item;
            foreach (var file in item.ContentItem?.MediaFiles ?? [])
            {
                context.Owners[file.Id] = item.Id;
            }
        }

        foreach (var container in items.Where(IsContainer))
        {
            await PlanRebindAsync(container, context, cancellationToken).ConfigureAwait(false);
        }

        foreach (var leaf in context.Items.Values.Where(item => !IsContainer(item)).ToList())
        {
            await PlanDuplicatesAsync(leaf, context, cancellationToken).ConfigureAwait(false);
            await PlanCoverageAsync(leaf, context, cancellationToken).ConfigureAwait(false);
        }

        await PlanReassignCoverageAsync(context, cancellationToken).ConfigureAwait(false);
        return context.Plan;
    }

    public async Task<IReadOnlyList<MediaFileCleanupRowResult>> ApplyAsync(
        MediaFileCleanupApplyRequest request,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<MediaFileCleanupRowResult>();
        var reassignRows = request.Rows.Where(row => row.Action == MediaFileCleanupAction.Reassign).ToList();
        var coverRows = request.Rows.Where(row => row.Action == MediaFileCleanupAction.Cover).ToList();
        if (reassignRows.Count > 0 || coverRows.Count > 0)
        {
            var plan = await PlanAsync(request.RootId, cancellationToken).ConfigureAwait(false);
            foreach (var row in reassignRows)
            {
                var planned = plan.Reassign.FirstOrDefault(candidate => MatchesTarget(candidate, row));
                results.Add(await RunAsync(
                        row,
                        planned,
                        () => transactionManager.ExecuteWithTransactionAsync(
                            async ct => await contentService.ReassignFileAsync(
                                    planned!.CurrentItemId,
                                    planned.MediaFileId,
                                    new()
                                    {
                                        TargetContentHierarchyItemId = planned.TargetItemId!.Value,
                                        ExpectedFilePath = planned.FilePath
                                    },
                                    actingUserId,
                                    ct)
                                .ConfigureAwait(false) &&
                                await CoverAsync(planned, actingUserId, ct).ConfigureAwait(false),
                            cancellationToken))
                    .ConfigureAwait(false));
            }

            foreach (var row in coverRows)
            {
                var planned = plan.Cover.FirstOrDefault(candidate => MatchesTarget(candidate, row));
                results.Add(await RunAsync(
                        row,
                        planned,
                        () => transactionManager.ExecuteWithTransactionAsync(
                            ct => CoverAsync(planned!, actingUserId, ct),
                            cancellationToken))
                    .ConfigureAwait(false));
            }
        }

        var removalRows = request.Rows
            .Where(row => row.Action is MediaFileCleanupAction.Unlink or MediaFileCleanupAction.Delete)
            .ToList();
        if (removalRows.Count > 0)
        {
            var plan = await BuildPlanAsync(request.RootId, rankPlannedReassigns: false, cancellationToken)
                .ConfigureAwait(false);
            var dryRun = await PlanAsync(request.RootId, cancellationToken).ConfigureAwait(false);
            foreach (var row in removalRows)
            {
                bool Matches(MediaFileCleanupRow candidate) =>
                    candidate.MediaFileId == row.MediaFileId &&
                    candidate.FilePath == row.FilePath &&
                    candidate.KeepMediaFileId == row.KeepMediaFileId;
                var planned = dryRun.Unlink.Any(Matches) ? plan.Unlink.FirstOrDefault(Matches) : null;
                if (planned is not null &&
                    row.Action == MediaFileCleanupAction.Delete &&
                    (planned.KeepFilePath is not { } keepFilePath || !fileSystem.FileExists(keepFilePath)))
                {
                    results.Add(new()
                    {
                        MediaFileId = row.MediaFileId,
                        Action = row.Action,
                        Message = planned.KeepFilePath is null
                            ? "No copy stays in place of this file, so it is not deleted."
                            : $"The copy that stays, {planned.KeepFilePath}, is not on disk, so this file is " +
                                "not deleted."
                    });
                    continue;
                }

                results.Add(await RunAsync(
                        row,
                        planned,
                        () => row.Action == MediaFileCleanupAction.Delete
                            ? DeleteMediaFileAsync(
                                planned!.CurrentItemId,
                                planned.MediaFileId,
                                actingUserId,
                                cancellationToken)
                            : contentService.UnlinkFileAsync(
                                planned!.CurrentItemId,
                                planned.MediaFileId,
                                actingUserId,
                                cancellationToken))
                    .ConfigureAwait(false));
            }
        }

        results.AddRange(request.Rows
            .Where(row => row.Action == MediaFileCleanupAction.Review)
            .Select(row => new MediaFileCleanupRowResult
            {
                MediaFileId = row.MediaFileId,
                Action = row.Action,
                Message = "A review row needs a decision, so apply skips it."
            }));
        return results;
    }

    private static bool MatchesTarget(MediaFileCleanupRow planned, MediaFileCleanupRow row) =>
        planned.MediaFileId == row.MediaFileId &&
        planned.FilePath == row.FilePath &&
        planned.TargetItemId == row.TargetItemId &&
        planned.CoveredItemIds.SequenceEqual(row.CoveredItemIds);

    private static async Task<MediaFileCleanupRowResult> RunAsync(
        MediaFileCleanupRow row,
        MediaFileCleanupRow? planned,
        Func<Task<bool>> action)
    {
        var result = new MediaFileCleanupRowResult { MediaFileId = row.MediaFileId, Action = row.Action };
        if (planned is null)
        {
            return result with
            {
                Message = row.Action == MediaFileCleanupAction.Delete
                    ? "The current plan does not supersede this file, so it is not deleted."
                    : "The row no longer matches the current plan."
            };
        }

        try
        {
            return await action().ConfigureAwait(false)
                ? result with { Applied = true }
                : result with { Message = "The file or its item no longer exists." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return result with { Message = ex.Message };
        }
    }

    private async Task<bool> CoverAsync(
        MediaFileCleanupRow row,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        foreach (var itemId in row.CoveredItemIds)
        {
            var item = await contentHierarchyItemRepository.GetByIdAsync(itemId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Content hierarchy item {itemId} no longer exists.");
            if (row.Primary && !HasNoPrimaryFile(item))
            {
                throw new InvalidOperationException($"The {item.Level} '{item.Name}' already has a primary file.");
            }

            await contentService.AttachFileAsync(itemId, row.FilePath, actingUserId, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> LoadItemsWithFilesAsync(
        Guid? rootId,
        CancellationToken cancellationToken)
    {
        var items = (await contentHierarchyItemRepository.QueryAllAsync(
                new ContentHierarchyItemCriteria
                {
                    AncestorId = rootId,
                    MediaFiles = new() { Some = [[new MediaFileCriteria()]] }
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false)).ToList();
        var root = rootId is { } id
            ? await contentHierarchyItemRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
        if (root is not null && root.ContentItem?.MediaFiles.Count > 0)
        {
            items.Insert(0, root);
        }

        return items;
    }

    private bool IsContainer(ContentHierarchyItem item) =>
        contentTypeRegistry.GetChildLevels(item.ContentType, item.Level).Count > 0;

    private bool Accepts(ContentHierarchyLevelDefinition level, MediaFile file) =>
        (level.AcceptedFileTypes.Count > 0 &&
            fileSystem.MatchesAnyFileType(file.FilePath, file.MimeType, level.AcceptedFileTypes)) ||
        fileSystem.MatchesAnyFileType(file.FilePath, file.MimeType, level.AcceptedSidecarTypes);

    private ContentHierarchyLevelDefinition? LevelOf(ContentHierarchyItem item) =>
        contentTypeRegistry.GetDefinitionFor(item.ContentType)?.LevelDefinitions
            .FirstOrDefault(level => level.Slug == item.Level);

    private async Task PlanRebindAsync(
        ContentHierarchyItem container,
        PlanContext context,
        CancellationToken cancellationToken)
    {
        var leafLevels = contentTypeRegistry.GetDefinitionFor(container.ContentType)?.LevelDefinitions
            .Where(level => contentTypeRegistry.GetChildLevels(container.ContentType, level.Slug).Count == 0)
            .ToList() ?? [];
        var provider = contentTypeRegistry.FindProviderFor(container.ContentType);
        var ownLevel = LevelOf(container);
        foreach (var file in container.ContentItem?.MediaFiles ?? [])
        {
            if (ownLevel is not null && Accepts(ownLevel, file))
            {
                continue;
            }

            var primary = leafLevels.Any(level => level.AcceptedFileTypes.Count > 0 &&
                fileSystem.MatchesAnyFileType(file.FilePath, file.MimeType, level.AcceptedFileTypes));
            if (!primary && !leafLevels.Any(level =>
                    fileSystem.MatchesAnyFileType(file.FilePath, file.MimeType, level.AcceptedSidecarTypes)))
            {
                continue;
            }

            var row = new MediaFileCleanupRow
            {
                MediaFileId = file.Id,
                FilePath = file.FilePath,
                Primary = primary,
                CurrentItemId = container.Id,
                CurrentItem = await NameAsync(container.Id, context, cancellationToken).ConfigureAwait(false),
                Action = MediaFileCleanupAction.Review,
                Reason = string.Empty,
            };
            try
            {
                var route = await RouteFileAsync(
                        container,
                        file.FilePath,
                        RootFolderContentDiscoveryJob.ToSignalDictionary(provider?.Parse(file.FilePath, out _)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (route.StoppedEarly)
                {
                    context.Plan.Review.Add(row with
                    {
                        Reason = $"The binding matched no single item below {route.Node.Level} '{route.Node.Name}'."
                    });
                    continue;
                }

                context.Plan.Reassign.Add(row with
                {
                    TargetItemId = route.Node.Id,
                    TargetItem = await NameAsync(route.Node.Id, context, cancellationToken).ConfigureAwait(false),
                    Action = MediaFileCleanupAction.Reassign,
                    Reason = $"The binding matched {route.Node.Level} '{route.Node.Name}'."
                });
                context.Covered[file.Id] = route.Covered;
                context.Items.TryAdd(route.Node.Id, route.Node);
                if (!context.Incoming.TryGetValue(route.Node.Id, out var incoming))
                {
                    context.Incoming[route.Node.Id] = incoming = [];
                }

                incoming.Add(file);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.Plan.Review.Add(row with { Reason = ex.Message });
            }
        }
    }

    private async Task PlanReassignCoverageAsync(PlanContext context, CancellationToken cancellationToken)
    {
        for (var index = 0; index < context.Plan.Reassign.Count; index++)
        {
            var row = context.Plan.Reassign[index];
            if (context.Covered.GetValueOrDefault(row.MediaFileId) is not { Count: > 0 } covered ||
                context.UnlinksOrReviews(row.MediaFileId))
            {
                continue;
            }

            var added = covered.Where(HasNoPrimaryFile).ToList();
            context.Plan.Reassign[index] = row with
            {
                CoveredItemIds = [.. added.Select(item => item.Id)],
                CoveredItems = await NamesAsync(added, context, cancellationToken).ConfigureAwait(false),
                Reason = row.Reason + CoverageReason(added, [.. covered.Except(added)])
            };
        }
    }

    private async Task PlanCoverageAsync(
        ContentHierarchyItem leaf,
        PlanContext context,
        CancellationToken cancellationToken)
    {
        if (leaf.ParentId is not { } parentId ||
            LevelOf(leaf) is not { } level ||
            contentTypeRegistry.FindProviderFor(leaf.ContentType) is not
                IFileRoutingContentTypeDefinitionProvider provider)
        {
            return;
        }

        foreach (var file in (leaf.ContentItem?.MediaFiles ?? [])
            .Where(file => FileSystem.IsPrimaryFile(fileSystem, level, file) && !context.UnlinksOrReviews(file.Id)))
        {
            var signals = RootFolderContentDiscoveryJob.ToSignalDictionary(provider.Parse(file.FilePath, out _));
            var namesSeveralItems = signals.Values.Any(values => values.Count > 1);
            if (!namesSeveralItems ||
                await contentHierarchyItemRepository.GetByIdAsync(parentId, cancellationToken).ConfigureAwait(false)
                    is not { } parent)
            {
                continue;
            }

            var siblings = await ListDirectChildrenAsync(parentId, cancellationToken).ConfigureAwait(false);
            var named = await ResolveFileMatchAsync(parent, siblings, file.FilePath, signals, cancellationToken)
                .ConfigureAwait(false);
            var review = named.Count == 0
                ? $"The binding matched no single item below {parent.Level} '{parent.Name}'."
                : named.All(item => item.Id != leaf.Id)
                    ? $"The file name does not name {leaf.Level} '{leaf.Name}', which owns the file."
                    : null;
            if (review is not null)
            {
                context.Plan.Review.Add(await RowAsync(
                        file,
                        true,
                        null,
                        null,
                        MediaFileCleanupAction.Review,
                        review,
                        context,
                        cancellationToken)
                    .ConfigureAwait(false));
                continue;
            }

            var others = named.Where(item => item.Id != leaf.Id &&
                    !file.Coverage.Any(coverage => coverage.ContentItemId == item.ContentItem?.Id))
                .ToList();
            var added = others.Where(HasNoPrimaryFile).ToList();
            if (added.Count == 0)
            {
                continue;
            }

            var leftOut = others.Except(added).ToList();
            var owner = named.Except(leftOut).First();
            var row = await RowAsync(
                    file,
                    true,
                    null,
                    null,
                    MediaFileCleanupAction.Cover,
                    CoverageReason(added, leftOut).TrimStart(),
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
            context.Plan.Cover.Add(row with
            {
                TargetItemId = owner.Id,
                TargetItem = await NameAsync(owner.Id, context, cancellationToken).ConfigureAwait(false),
                CoveredItemIds = [.. added.Select(item => item.Id)],
                CoveredItems = await NamesAsync(added, context, cancellationToken).ConfigureAwait(false),
            });
        }
    }

    private async Task PlanDuplicatesAsync(
        ContentHierarchyItem leaf,
        PlanContext context,
        CancellationToken cancellationToken)
    {
        if (LevelOf(leaf) is not { } level)
        {
            return;
        }

        var incoming = context.RankPlannedReassigns ? context.Incoming.GetValueOrDefault(leaf.Id) ?? [] : [];
        var files = (leaf.ContentItem?.MediaFiles ?? []).Concat(incoming).ToList();
        var primaries = files.Where(file => FileSystem.IsPrimaryFile(fileSystem, level, file))
            .OrderBy(file => file.FilePath, StringComparer.Ordinal)
            .ToList();
        if (primaries.Count < 2)
        {
            return;
        }

        var folder = await contentFileService.GetEffectivePathAsync(leaf.Id, cancellationToken).ConfigureAwait(false);
        var longest = primaries.Where(IsVideo).Max(file => file.MediaInfo?.DurationSeconds);
        var marked = primaries
            .Where(file => IsExtra(
                    folder is not null && fileSystem.IsPathWithin(folder, file.FilePath) ? folder : null,
                    file.FilePath) ||
                (IsVideo(file) && file.MediaInfo?.DurationSeconds < longest / 2))
            .ToList();
        var extras = marked.Count < primaries.Count ? marked : [];
        foreach (var extra in extras)
        {
            context.Plan.Unlink.Add(await RowAsync(
                    extra,
                    true,
                    null,
                    null,
                    MediaFileCleanupAction.Unlink,
                    "The file is an extra.",
                    context,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        var copies = primaries.Except(extras).ToList();
        var (keep, unlinked) = copies.Count < 2
            ? (null, [])
            : await PlanCopiesAsync(leaf, copies, context, cancellationToken).ConfigureAwait(false);
        var unlinkedPaths = extras.Concat(unlinked).Select(file => file.FilePath).ToList();
        var stayingPaths = copies.Except(unlinked).Select(file => file.FilePath).ToList();
        var sidecars = files
            .Where(file => !FileSystem.IsPrimaryFile(fileSystem, level, file))
            .DistinctBy(file => file.Id)
            .Select(file => (
                File: file,
                Primary: DownloadImportItemJob.FindPrimary(file.FilePath, unlinkedPaths)))
            .Where(pair => pair.Primary is not null &&
                DownloadImportItemJob.FindPrimary(pair.File.FilePath, stayingPaths) is null);
        foreach (var (sidecar, primaryPath) in sidecars)
        {
            context.Plan.Unlink.Add(await RowAsync(
                    sidecar,
                    false,
                    null,
                    unlinked.Any(file => file.FilePath == primaryPath) ? keep : null,
                    MediaFileCleanupAction.Unlink,
                    $"The sidecar belongs to {primaryPath}, which this plan unlinks.",
                    context,
                    cancellationToken)
                .ConfigureAwait(false));
        }
    }

    private async Task<(MediaFile? Keep, List<MediaFile> Unlinked)> PlanCopiesAsync(
        ContentHierarchyItem leaf,
        IReadOnlyList<MediaFile> copies,
        PlanContext context,
        CancellationToken cancellationToken)
    {
        var ranks = new Dictionary<Guid, int?>();
        foreach (var file in copies)
        {
            var score = await releaseEvaluationService.ScoreFileForContentAsync(file, leaf.Id, cancellationToken)
                .ConfigureAwait(false);
            ranks[file.Id] = score?.MatchedRankOrdinal;
        }

        var best = ranks.Values.Min();
        if (best is null)
        {
            foreach (var file in copies)
            {
                context.Plan.Review.Add(await RowAsync(
                        file,
                        true,
                        null,
                        null,
                        MediaFileCleanupAction.Review,
                        "No copy of this item has a quality rank, so the plan cannot choose the copy to keep.",
                        context,
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            return (null, []);
        }

        var bestFiles = copies.Where(file => ranks[file.Id] == best).ToList();
        var keep = bestFiles[0];
        if (bestFiles.Count > 1)
        {
            foreach (var file in bestFiles)
            {
                context.Plan.Review.Add(await RowAsync(
                        file,
                        true,
                        best,
                        null,
                        MediaFileCleanupAction.Review,
                        $"{bestFiles.Count} copies share the best rank {best}; choose the copy to keep.",
                        context,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        var rootFolder = await rootFolderRepository.GetByIdAsync(leaf.EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false);
        var unlinked = new List<MediaFile>();
        foreach (var file in copies.Where(file => ranks[file.Id] != best))
        {
            var rank = ranks[file.Id];
            if (rank is null)
            {
                context.Plan.Review.Add(await RowAsync(
                        file,
                        true,
                        null,
                        keep,
                        MediaFileCleanupAction.Review,
                        "The copy has no quality rank, so the plan cannot compare it with the others.",
                        context,
                        cancellationToken)
                    .ConfigureAwait(false));
                continue;
            }

            if (rootFolder?.UpgradeFileHandling != UpgradeFileHandling.Replace)
            {
                context.Plan.Review.Add(await RowAsync(
                        file,
                        true,
                        rank,
                        keep,
                        MediaFileCleanupAction.Review,
                        $"Rank {rank} is worse than rank {best} of the copy that stays, but the root folder keeps " +
                        "both copies of an upgrade.",
                        context,
                        cancellationToken)
                    .ConfigureAwait(false));
                continue;
            }

            context.Plan.Unlink.Add(await RowAsync(
                    file,
                    true,
                    rank,
                    keep,
                    MediaFileCleanupAction.Unlink,
                    $"Rank {rank} is worse than rank {best} of the copy that stays.",
                    context,
                    cancellationToken)
                .ConfigureAwait(false));
            unlinked.Add(file);
        }

        return (keep, unlinked);
    }

    private async Task<MediaFileCleanupRow> RowAsync(
        MediaFile file,
        bool primary,
        int? rank,
        MediaFile? keep,
        MediaFileCleanupAction action,
        string reason,
        PlanContext context,
        CancellationToken cancellationToken)
    {
        var ownerId = context.Owners[file.Id];
        return new MediaFileCleanupRow
        {
            MediaFileId = file.Id,
            FilePath = file.FilePath,
            Primary = primary,
            CurrentItemId = ownerId,
            CurrentItem = await NameAsync(ownerId, context, cancellationToken).ConfigureAwait(false),
            Action = action,
            Reason = reason,
            Rank = rank,
            KeepMediaFileId = keep?.Id,
            KeepFilePath = keep?.FilePath,
        };
    }

    private async Task<string> NameAsync(Guid itemId, PlanContext context, CancellationToken cancellationToken)
    {
        if (!context.Names.TryGetValue(itemId, out var name))
        {
            var lineage = await contentHierarchyItemRepository.GetLineageAsync(itemId, cancellationToken)
                .ConfigureAwait(false);
            name = string.Join(" / ", lineage.Reverse().Select(item => item.Name));
            context.Names[itemId] = name;
        }

        return name;
    }

    private async Task<List<string>> NamesAsync(
        IReadOnlyList<ContentHierarchyItem> items,
        PlanContext context,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        foreach (var item in items)
        {
            names.Add(await NameAsync(item.Id, context, cancellationToken).ConfigureAwait(false));
        }

        return names;
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

    private static string CoverageReason(
        IReadOnlyList<ContentHierarchyItem> added,
        IReadOnlyList<ContentHierarchyItem> leftOut) =>
        (added.Count > 0 ? $" The file name also covers {Describe(added)}." : string.Empty) +
        leftOut.Count switch
        {
            0 => string.Empty,
            1 => $" The file does not cover {Describe(leftOut)}, which already has a primary file.",
            _ => $" The file does not cover {Describe(leftOut)}; each already has a primary file.",
        };

    private static string Describe(IEnumerable<ContentHierarchyItem> items) =>
        string.Join(", ", items.Select(item => $"{item.Level} '{item.Name}'"));

    private static ContentHierarchyItem? ResolveByTitleSignal(
        IReadOnlyList<ContentHierarchyItem> children,
        string? parsedTitle)
    {
        if (string.IsNullOrWhiteSpace(parsedTitle))
        {
            return null;
        }

        var title = TitleMatchText(parsedTitle);
        var matches = children
            .Where(child => string.Equals(TitleMatchText(child.Name), title, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string TitleMatchText(string value) =>
        FilesystemSubstitutePattern.Replace(value.NormalizeMediaFilename(), " ").Trim();

    private async Task<FileRouteResult> RouteFileAsync(
        ContentHierarchyItem root,
        string filePath,
        Dictionary<string, List<string>> signals,
        CancellationToken cancellationToken)
    {
        var current = root;
        IReadOnlyList<ContentHierarchyItem> covered = [];

        while (true)
        {
            var children = await ListDirectChildrenAsync(current.Id, cancellationToken).ConfigureAwait(false);
            if (children.Count == 0)
            {
                var childLevels = contentTypeRegistry.GetChildLevels(current.ContentType, current.Level);
                if (childLevels.Count > 0)
                {
                    var levels = string.Join(", ", childLevels.Select(level => level.Slug));

                    if (current.PendingMetadataRefresh)
                    {
                        throw new ChildLevelPendingException(
                            $"The import is waiting until the {levels} level below {current.Level} " +
                            $"'{current.Name}' exists.",
                            current.Id);
                    }

                    throw new InvalidOperationException(
                        $"The target {levels} level does not exist yet below {current.Level}.");
                }

                return new(current, covered, StoppedEarly: false);
            }

            var next = await ResolveFileMatchAsync(current, children, filePath, signals, cancellationToken)
                .ConfigureAwait(false);
            if (next.Count == 0)
            {
                return new(current, [], StoppedEarly: true);
            }

            current = next[0];
            covered = [.. next.Skip(1)];
        }
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> ResolveFileMatchAsync(
        ContentHierarchyItem parent,
        IReadOnlyList<ContentHierarchyItem> children,
        string filePath,
        Dictionary<string, List<string>> signals,
        CancellationToken cancellationToken)
    {
        if (contentTypeRegistry.FindProviderFor(parent.ContentType) is
            IFileRoutingContentTypeDefinitionProvider fileRouter)
        {
            return [.. fileRouter.ResolveFileChildren(parent, children, signals)
                .Join(children, id => id, child => child.Id, (_, child) => child)];
        }

        var release = new Release { Protocol = "file", Title = Path.GetFileName(filePath) };
        var fileRef = new ReleaseFileRef(filePath, Path.GetFileName(filePath));
        var assignments = await releaseEvaluationService
            .AssignFilesToLeavesAsync(release, parent, children, [fileRef], cancellationToken)
            .ConfigureAwait(false);
        var leafId = assignments.FirstOrDefault()?.LeafContentHierarchyItemId;
        var leaf = (leafId is { } id ? children.FirstOrDefault(child => child.Id == id) : null)
            ?? ResolveByTitleSignal(
                [.. children.Where(IsContainer)],
                FirstNonEmptySignal(signals, ParsedFileSignalKeys.Title));
        return leaf is null ? [] : [leaf];
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
        bool associate,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var root = associate
            ? await FindLibraryRootAsync(candidate, match, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"The library has no item for {match.ExternalIdKey}={match.ExternalId} in this root folder. " +
                    "No content was created and no file was attached.")
            : await HydrateForImportAsync(candidate, match, monitoringMode, actingUserId, cancellationToken)
                .ConfigureAwait(false);
        if (root is null)
        {
            return null;
        }

        var routed = await RouteFileAsync(root, candidate.FilePath, candidate.ParsedSignals, cancellationToken)
            .ConfigureAwait(false);
        return await AttachRoutedFileAsync(
                candidate,
                match,
                routed,
                associate,
                actingUserId,
                correlationId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ContentHierarchyItem?> HydrateForImportAsync(
        LibraryImportCandidate candidate,
        LibraryImportMatch match,
        LibraryImportMonitoringMode monitoringMode,
        Guid? actingUserId,
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

        return hydrated.Value.Root;
    }

    private async Task<(Guid HierarchyItemId, Guid ContentItemId)?> AttachRoutedFileAsync(
        LibraryImportCandidate candidate,
        LibraryImportMatch match,
        FileRouteResult routed,
        bool associate,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (routed.StoppedEarly &&
            !(LevelOf(routed.Node) is { AcceptedFileTypes.Count: > 0 } level &&
                await fileSystem.MatchesAnyFileTypeAsync(candidate.FilePath, level.AcceptedFileTypes, cancellationToken)
                    .ConfigureAwait(false)))
        {
            throw new InvalidOperationException(
                $"The file assignment is unresolved below {routed.Node.Level} '{routed.Node.Name}'. " +
                "No file was attached.");
        }

        var hierarchyItemId = routed.Node.Id;

        var keepFolder = associate && (await contentHierarchyItemRepository
            .GetByIdAsync(hierarchyItemId, cancellationToken)
            .ConfigureAwait(false))?.ContentItem?.MediaFiles.Count > 0;
        var discoveredPath = keepFolder
            ? null
            : await ResolveDiscoveredPathAsync(hierarchyItemId, candidate.FilePath, cancellationToken)
                .ConfigureAwait(false);

        var contentItemId = await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var lineage = await contentHierarchyItemRepository.GetLineageAsync(hierarchyItemId, ct)
                    .ConfigureAwait(false);
                if (lineage.Count > 0)
                {
                    await contentHierarchyItemRepository.LockAsync(lineage[^1].Id, ct).ConfigureAwait(false);
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                if (discoveredPath is not null)
                {
                    if (discoveredPath.ContentHierarchyItemId != discoveredPath.RootId)
                    {
                        await contentHierarchyItemRepository
                            .LockAsync(discoveredPath.ContentHierarchyItemId, ct).ConfigureAwait(false);
                    }

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
                    var alreadyDiscovered = pathOwner.Path is not null &&
                        fileSystem.PathsEqual(pathOwner.Path, discoveredPath.AbsolutePath);

                    var refreshedYear = pathOwner.MetadataContainer?.Contributions
                        .OrderBy(contribution => contribution.Ordinal)
                        .ThenBy(contribution => contribution.ProviderType, StringComparer.Ordinal)
                        .Select(contribution => contribution.PublishDate?.Year)
                        .FirstOrDefault(year => year is not null);
                    var refreshedPlaceholder = discoveredPath.WasMetadataPlaceholder &&
                        !pathOwner.PendingMetadataRefresh &&
                        pathOwner.DateLastRefreshed is not null &&
                        !string.IsNullOrWhiteSpace(pathOwner.Name) &&
                        string.Equals(
                            pathOwner.Path,
                            pathOwner.Name.ToGeneratedContentPath(refreshedYear),
                            StringComparison.Ordinal);
                    if (!canReplacePath ||
                        !alreadyDiscovered && !refreshedPlaceholder &&
                        !string.Equals(pathOwner.Path, discoveredPath.OriginalPath, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"The path of content hierarchy item {discoveredPath.ContentHierarchyItemId} " +
                            "changed during discovery.");
                    }

                    if (!alreadyDiscovered)
                    {
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
                }

                var attachment = await contentFileService
                    .AttachImportedAsync(hierarchyItemId, candidate.FilePath, actingUserId, ct)
                    .ConfigureAwait(false);
                var attached = attachment.Result?.MediaFile
                    ?? throw new KeyNotFoundException($"Content hierarchy item {hierarchyItemId} no longer exists.");
                foreach (var covered in routed.Covered)
                {
                    if (await HasNoPrimaryFileAsync(covered.Id, ct).ConfigureAwait(false))
                    {
                        await contentFileService.AttachAsync(covered.Id, candidate.FilePath, actingUserId, ct)
                            .ConfigureAwait(false);
                    }
                }

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
                return attached.ContentItemId;
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
                    Patch = new()
                    {
                        Monitoring = new ScalarPatch<MonitoringMode?> { Value = MonitoringMode.Monitored }
                    },
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
                    Patch = new() { Monitoring = new ScalarPatch<MonitoringMode?> { Value = null } },
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
                Patch = new() { Monitoring = new ScalarPatch<MonitoringMode?> { Value = MonitoringMode.Monitored } },
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

        var wasMetadataPlaceholder = owner.PendingMetadataRefresh &&
            !string.IsNullOrWhiteSpace(owner.Path) &&
            string.Equals(owner.Path, owner.Slug.NormalizeContentPathSegment(), StringComparison.Ordinal);
        return new DiscoveredPath(owner.Id, lineage[^1].Id, owner.Path, fileDirectory, wasMetadataPlaceholder);
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

    /// <inheritdoc />
    public async Task<FileRenamePreview> PreviewRenamesAsync(
        LibraryImportRenameSelection selection,
        CancellationToken cancellationToken = default)
    {
        var ids = selection.Ids.ToHashSet();
        foreach (var filter in selection.Filters)
        {
            ids.UnionWith(await LoadCandidateIdsAsync(filter, cancellationToken).ConfigureAwait(false));
        }

        var files = new List<FileRenameEntry>();
        var routed = new List<(
            Guid CandidateId,
            string Path,
            IReadOnlyList<ContentHierarchyItem> Lineage,
            IReadOnlyList<ContentHierarchyItem> Covered)>();
        var hasMask = false;
        foreach (var id in ids)
        {
            var candidate = await libraryImportCandidateRepository.GetByIdAsync(id, cancellationToken)
                .ConfigureAwait(false);
            var entry = new FileRenameEntry { CandidateId = id, OriginalPath = candidate?.FilePath ?? string.Empty };
            try
            {
                if (candidate is null || candidate.Matches.Count == 0)
                {
                    throw new InvalidOperationException("The import candidate has no available metadata match.");
                }

                hasMask |= await contentFileService.HasRenameMaskAsync(candidate.RootFolderId, cancellationToken)
                    .ConfigureAwait(false);

                var match = candidate.Matches[0];
                var nodes = await contentService.PreviewImportHierarchyAsync(
                    new PreviewByExternalIdRequest
                    {
                        ProviderType = match.ProviderType,
                        ExternalIdKey = match.ExternalIdKey,
                        ExternalId = match.ExternalId,
                        ContentType = candidate.ContentType,
                        LevelSlug = match.LevelSlug
                    },
                    candidate.RootFolderId,
                    cancellationToken).ConfigureAwait(false);
                var current = nodes[0];
                var lineage = new List<ContentHierarchyItem> { current };
                var ancestor = current;
                while (ancestor.ParentId is { } parentId && nodes.FirstOrDefault(node => node.Id == parentId)
                    is { } parent)
                {
                    lineage.Add(parent);
                    ancestor = parent;
                }

                IReadOnlyList<ContentHierarchyItem> covered = [];
                while (true)
                {
                    var children = nodes.Where(node => node.ParentId == current.Id).ToArray();
                    if (children.Length == 0)
                    {
                        var childLevels = contentTypeRegistry.GetChildLevels(current.ContentType, current.Level);
                        if (childLevels.Count > 0 && current.PendingMetadataRefresh)
                        {
                            throw new InvalidOperationException(
                                $"The {string.Join(", ", childLevels.Select(level => level.Slug))} level below " +
                                $"{current.Level} '{current.Name}' does not exist yet because its metadata " +
                                "refresh is pending.");
                        }

                        if (childLevels.Count > 0)
                        {
                            throw new InvalidOperationException("The target child level does not exist yet.");
                        }

                        break;
                    }

                    var next = await ResolveFileMatchAsync(
                            current,
                            children,
                            candidate.FilePath,
                            candidate.ParsedSignals,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (next.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"The file assignment is unresolved below {current.Level}.");
                    }

                    current = next[0];
                    covered = [.. next.Skip(1)];
                    lineage.Insert(0, current);
                }

                var preview = await contentFileService.PreviewImportRenameMaskAsync(
                    lineage,
                    covered,
                    candidate.FilePath,
                    cancellationToken).ConfigureAwait(false);
                hasMask |= preview.HasMask;
                files.AddRange(preview.Files.Select(file => file with { CandidateId = id }));
                routed.Add((id, candidate.FilePath, lineage, covered));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                files.Add(entry with { Error = ex.Message });
            }
        }

        var primaryPlans = files.Where(file => !file.IsSidecar).ToArray();
        foreach (var route in routed.Where(route => primaryPlans.Count(file =>
            file.ContentHierarchyItemId == route.Lineage[0].Id) > 1))
        {
            var combined = await contentFileService.PreviewImportRenameMaskAsync(
                route.Lineage,
                route.Covered,
                route.Path,
                cancellationToken,
                primaryPlans.Where(file =>
                    file.ContentHierarchyItemId == route.Lineage[0].Id).ToArray()).ConfigureAwait(false);
            files.RemoveAll(file => file.CandidateId == route.CandidateId);
            files.AddRange(combined.Files.Select(file => file with { CandidateId = route.CandidateId }));
        }

        return new()
        {
            HasMask = hasMask,
            Files = files.Select(file => file.DestinationPath is not null &&
            files.Any(other => other.CandidateId != file.CandidateId && other.DestinationPath is not null &&
                !(file.IsSidecar && other.IsSidecar && file.MediaFileId.HasValue &&
                    file.MediaFileId == other.MediaFileId) &&
                fileSystem.PathsEqual(file.DestinationPath, other.DestinationPath))
                ? file with { Error = "Multiple selected files have the same destination." }
                : file).ToArray()
        };
    }

    /// <inheritdoc />
    public async Task<LibraryImportCompletionResult> ImportWithRenameAsync(
        Guid candidateId,
        int matchIndex,
        LibraryImportMonitoringMode monitoringMode,
        FileRenamePreview preview,
        Guid? actingUserId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await libraryImportCandidateRepository.GetByIdAsync(candidateId, cancellationToken)
            .ConfigureAwait(false);
        var result = await ImportCandidateAsync(
            candidateId,
            matchIndex,
            monitoringMode,
            actingUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        if (result.Status != LibraryImportCompletionStatus.Completed || result.ContentHierarchyItemId is not { } id)
        {
            return result;
        }

        var acceptedFile = preview.Files.FirstOrDefault(file => file.CandidateId == candidateId &&
            candidate is not null && fileSystem.PathsEqual(candidate.FilePath, file.OriginalPath));
        if (acceptedFile is null || candidate is null ||
            !fileSystem.PathsEqual(candidate.FilePath, acceptedFile.OriginalPath))
        {
            return result with
            {
                Message = "Association completed. Rename failed: the import file changed after preview."
            };
        }

        try
        {
            var current = await contentFileService.PreviewRenameMaskAsync(id, new(), cancellationToken)
                .ConfigureAwait(false);
            var entries = preview.Files.Select(entry => entry with
            {
                ContentHierarchyItemId = id,
                MediaFileId = current.Files.FirstOrDefault(file =>
                    fileSystem.PathsEqual(file.OriginalPath, entry.OriginalPath))?.MediaFileId
            }).ToArray();
            var renamed = await contentFileService.ExecuteRenameMaskAsync(
                id,
                new() { HasMask = true, Files = entries },
                actingUserId,
                cancellationToken).ConfigureAwait(false);
            return result with
            {
                Message = renamed.Files.All(file => file.Completed)
                    ? "Association and explicit rename completed."
                    : "Association completed. Rename failed: " +
                        string.Join(
                            "; ",
                            renamed.Files.Where(file => file.Error is not null)
                                .Select(file => file.Error))
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Association completed but explicit rename failed for candidate {CandidateId}",
                candidateId);
            return result with { Message = "Association completed. Rename failed: " + ex.Message };
        }
    }

    private sealed record DiscoveredPath(
        Guid ContentHierarchyItemId,
        Guid RootId,
        string? OriginalPath,
        string AbsolutePath,
        bool WasMetadataPlaceholder);

    private sealed record FileRouteResult(
        ContentHierarchyItem Node,
        IReadOnlyList<ContentHierarchyItem> Covered,
        bool StoppedEarly);

    private sealed class ChildLevelPendingException(string message, Guid contentHierarchyItemId) : Exception(message)
    {
        public Guid ContentHierarchyItemId { get; } = contentHierarchyItemId;
    }

    private sealed class PlanContext
    {
        public bool RankPlannedReassigns { get; init; }

        public MediaFileCleanupPlan Plan { get; } = new();

        public Dictionary<Guid, ContentHierarchyItem> Items { get; } = [];

        public Dictionary<Guid, Guid> Owners { get; } = [];

        public Dictionary<Guid, List<MediaFile>> Incoming { get; } = [];

        public Dictionary<Guid, string> Names { get; } = [];

        public Dictionary<Guid, IReadOnlyList<ContentHierarchyItem>> Covered { get; } = [];

        public bool UnlinksOrReviews(Guid mediaFileId) =>
            Plan.Unlink.Concat(Plan.Review).Any(row => row.MediaFileId == mediaFileId);
    }
}
