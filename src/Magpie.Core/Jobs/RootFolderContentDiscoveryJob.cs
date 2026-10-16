using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Content;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RootFolderContentDiscoveryJob(
    IRootFolderRepository rootFolderRepository,
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IMediaFileRepository mediaFileRepository,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IContentFileService contentFileService,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IContentTypeRegistry contentTypeRegistry,
    IJobManager jobManager,
    ILibraryImportService libraryImportService,
    IFileSystem fileSystem,
    IEventBus eventBus,
    IProgressReporter progressReporter,
    TimeProvider timeProvider,
    ILogger<RootFolderContentDiscoveryJob> logger) : IJob
{
    private enum RootFolderIdSource
    {
        Parameter,
        EventContext,
    }

    private sealed record ScanPlan(string BasePath, IReadOnlyList<string> AcceptedFileTypes, bool GroupingEnabled);

    private sealed record PlannedFile(ScanPlan Plan, string FilePath, Dictionary<string, List<string>> Signals);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.RootFolderContentDiscovery,
        DisplayName = "Root folder content discovery",
        Description = "Scans root folders for files to import.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.RootFolderId],
        CoalesceWideningFields = [MagpieJobs.Parameters.ContentHierarchyItemId],
        LockPartitionEventContextKeys = new Dictionary<string, string>
        {
            [MagpieJobs.Parameters.RootFolderId] = MagpieEvents.Keys.RootFolderId
        },
        Parameters =
        [
            new(
                MagpieJobs.Parameters.RootFolderId,
                "The root folder to scan. Leave it empty to queue a scan of each root folder.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The library item whose top-level folder to scan, instead of the full root folder.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.RecoveryInactivityMinutes,
                "Minutes without candidate activity before recovery checks for an inactive import execution.",
                DataType.Number,
                false,
                "60")
            {
                ValidateValue = value =>
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
                    minutes > 0
                        ? null : "must be a positive integer."
            }
        ],
    };

    private const int ListingProgressInterval = 1000;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static Dictionary<string, List<string>> ToSignalDictionary(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? signals)
    {
        if (signals is null)
        {
            return [];
        }

        return signals.ToDictionary(
            pair => pair.Key,
            pair => (
                from value in pair.Value
                where !string.IsNullOrWhiteSpace(value)
                select value.Trim())
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasSampleDirectorySegment(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) && directory
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("sample", StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasSearchableTitle(Dictionary<string, List<string>> signals) =>
        (signals.TryGetValue(ParsedFileSignalKeys.TitleToken, out var tokens) && tokens.Count > 0) ||
        (signals.TryGetValue(ParsedFileSignalKeys.Title, out var titles) && titles.Count > 0);

    private static bool HasMatchingTitle(
        Dictionary<string, List<string>> fileSignals,
        Dictionary<string, List<string>> releaseSignals)
    {
        var releaseTitles = releaseSignals
            .Where(pair => pair.Key is ParsedFileSignalKeys.Title or ParsedFileSignalKeys.TitleToken)
            .SelectMany(pair => pair.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return fileSignals
            .Where(pair => pair.Key is ParsedFileSignalKeys.Title or ParsedFileSignalKeys.TitleToken)
            .SelectMany(pair => pair.Value)
            .Any(releaseTitles.Contains);
    }

    private static bool TryResolveRootFolderId(JobContext context, out string raw, out RootFolderIdSource source)
    {
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.RootFolderId, out var rawParameter) &&
            !string.IsNullOrWhiteSpace(rawParameter))
        {
            raw = rawParameter;
            source = RootFolderIdSource.Parameter;
            return true;
        }

        if (context.Event is { Context: var eventContext } &&
            eventContext.TryGetValue(MagpieEvents.Keys.RootFolderId, out var rawFromEvent) &&
            !string.IsNullOrWhiteSpace(rawFromEvent))
        {
            raw = rawFromEvent;
            source = RootFolderIdSource.EventContext;
            return true;
        }

        raw = string.Empty;
        source = RootFolderIdSource.Parameter;
        return false;
    }

    private static bool IsLevelAtOrBelow(ContentTypeDefinition definition, string levelSlug, string rootLevelSlug)
    {
        var current = definition.LevelDefinitions.FirstOrDefault(level => level.Slug == levelSlug);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current is not null && visited.Add(current.Slug))
        {
            if (current.Slug == rootLevelSlug)
            {
                return true;
            }

            current = definition.LevelDefinitions.FirstOrDefault(level => level.Slug == current.ParentSlug);
        }

        return false;
    }

    private List<ScanPlan> BuildRootLevelScanPlans(
        RootFolder rootFolder,
        IContentTypeDefinitionProvider provider,
        ContentTypeDefinition definition)
    {
        var pathProvider = provider as IPathResolutionContentTypeDefinitionProvider;
        var plans = new List<ScanPlan>();
        foreach (var root in definition.LevelDefinitions.Where(level => level.ParentSlug is null))
        {
            var basePath = string.IsNullOrWhiteSpace(root.Subfolder)
                ? rootFolder.Path
                : Path.Combine(rootFolder.Path, root.Subfolder);
            var accepted = definition.LevelDefinitions
                .Where(level => IsLevelAtOrBelow(definition, level.Slug, root.Slug))
                .SelectMany(level => level.AcceptedFileTypes)
                .Where(fileType => !string.IsNullOrWhiteSpace(fileType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (accepted.Length > 0)
            {
                var chain = pathProvider?.ResolveDirectoryLevelChain(FindLeafLevelSlug(definition.Slug, root.Slug));
                plans.Add(new ScanPlan(basePath, accepted, chain is { Count: > 0 }));
            }
        }

        return plans;
    }

    private string FindLeafLevelSlug(string contentType, string levelSlug)
    {
        var current = levelSlug;
        while (true)
        {
            var children = contentTypeRegistry.GetChildLevels(contentType, current);
            if (children.Count != 1)
            {
                return current;
            }

            current = children[0].Slug;
        }
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!TryResolveRootFolderId(context, out var raw, out var source))
        {
            await QueueEachRootFolderAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        Guid? contentHierarchyItemId = null;
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.ContentHierarchyItemId, out var rawItemId) &&
            !string.IsNullOrWhiteSpace(rawItemId))
        {
            contentHierarchyItemId = Guid.TryParse(rawItemId, out var parsedItemId)
                ? parsedItemId
                : throw new ArgumentException("The content hierarchy item ID has an invalid format.");
        }

        if (!Guid.TryParse(raw, out var rootFolderId))
        {
            throw new ArgumentException(
                source is RootFolderIdSource.EventContext
                    ? "The job received an invalid root folder ID."
                    : "The root folder ID has an invalid format.");
        }

        var inactivityMinutes = 60;
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.RecoveryInactivityMinutes, out var rawMinutes) &&
            (!int.TryParse(rawMinutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out inactivityMinutes) ||
                inactivityMinutes <= 0))
        {
            throw new ArgumentException("Recovery inactivity minutes must be a positive integer.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var recoveryCutoff = inactivityMinutes >= (now - DateTime.MinValue).TotalMinutes
            ? DateTime.MinValue
            : now.AddMinutes(-inactivityMinutes);
        await ScanAsync(
            rootFolderId,
            contentHierarchyItemId,
            recoveryCutoff,
            context.ManualTriggerUserId,
            context.CorrelationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task QueueEachRootFolderAsync(JobContext context, CancellationToken cancellationToken)
    {
        var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rootFolder in rootFolders)
        {
            var parameters = new Dictionary<string, string>
            {
                [MagpieJobs.Parameters.RootFolderId] = rootFolder.Id.ToString("D")
            };
            if (context.Parameters.TryGetValue(MagpieJobs.Parameters.RecoveryInactivityMinutes, out var minutes))
            {
                parameters[MagpieJobs.Parameters.RecoveryInactivityMinutes] = minutes;
            }

            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = Spec.JobType,
                    Parameters = parameters,
                    Mode = JobExecutionMode.Queue,
                    UserId = context.ManualTriggerUserId,
                    CorrelationId = context.CorrelationId,
                },
                cancellationToken).ConfigureAwait(false);
        }

        progressReporter.Report(1, $"The job queued a scan of {rootFolders.Count()} root folder(s).");
    }

    private async Task<string?> ResolveScopePathAsync(
        Guid rootFolderId,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0 || lineage[^1].EffectiveRootFolderId != rootFolderId)
        {
            throw new ArgumentException("The content hierarchy item is not in the root folder.");
        }

        var path = await contentFileService.GetEffectivePathAsync(lineage[^1].Id, cancellationToken)
            .ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(path) && fileSystem.DirectoryExists(path)
            ? fileSystem.GetFullPath(path)
            : null;
    }

    private async Task ScanAsync(
        Guid rootFolderId,
        Guid? contentHierarchyItemId,
        DateTime recoveryCutoff,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        progressReporter.Report(0, "The scan will load the root folder.");

        var rootFolder = await rootFolderRepository.GetByIdAsync(rootFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (rootFolder is null)
        {
            throw new InvalidOperationException($"Root folder {rootFolderId} does not exist.");
        }

        var provider = contentTypeRegistry.FindProviderFor(rootFolder.ContentType);
        if (provider is null)
        {
            logger.LogWarning(
                "No definition provider exists for {ContentType}, so no scan ran",
                rootFolder.ContentType);
            progressReporter.Report(1, "The scan stopped because the content type has no definition provider.");
            return;
        }

        var definition = provider.Definition;
        var plans = BuildRootLevelScanPlans(rootFolder, provider, definition);
        if (plans.Count == 0)
        {
            logger.LogWarning("{ContentType} has no accepted file types, so no scan ran", rootFolder.ContentType);
            progressReporter.Report(1, "The scan stopped because the content type accepts no file types.");
            return;
        }

        var scanRunId = Guid.NewGuid();
        logger.LogInformation(
            "The job will start scan {ScanRunId} of root folder {RootFolderId}",
            scanRunId,
            rootFolderId);

        var scopePath = contentHierarchyItemId is { } itemId
            ? await ResolveScopePathAsync(rootFolderId, itemId, cancellationToken).ConfigureAwait(false)
            : null;
        if (scopePath is not null)
        {
            plans = [.. plans.Where(plan => fileSystem.IsPathWithin(fileSystem.GetFullPath(plan.BasePath), scopePath))];
        }

        await ReclaimStrandedImportsAsync(rootFolderId, recoveryCutoff, userId, cancellationToken)
            .ConfigureAwait(false);

        var linkedPaths = await LoadLinkedPathsAsync(
            plans.Select(plan => plan.BasePath),
            cancellationToken).ConfigureAwait(false);
        var existing = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery { RootFolderId = rootFolderId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var replaced = existing
            .Where(candidate => !IsKept(candidate, linkedPaths) && (scopePath is null ||
                fileSystem.IsPathWithin(scopePath, fileSystem.GetFullPath(candidate.FilePath))))
            .ToList();

        await libraryImportCandidateRepository
            .DeleteByRootFolderAsync(
                rootFolderId,
                [.. replaced.Select(candidate => candidate.Id)],
                userId,
                cancellationToken)
            .ConfigureAwait(false);

        var keptCandidates = existing.Except(replaced).ToList();
        var inFlightPaths = keptCandidates
            .Select(candidate => fileSystem.GetFullPath(candidate.FilePath))
            .ToHashSet(PathComparer);

        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.Report(null, "The scan will list the files.");

        var planned = new List<PlannedFile>();
        var seenPaths = new HashSet<string>(PathComparer);
        var scannedPaths = new List<string>();
        var listedCount = 0;
        foreach (var plan in plans)
        {
            var listingPath = scopePath ?? plan.BasePath;
            if (!fileSystem.DirectoryExists(listingPath))
            {
                logger.LogInformation(
                    "Root folder {RootFolderId} has no folder at {BasePath}, so the scan skipped it",
                    rootFolderId,
                    plan.BasePath);
                continue;
            }

            scannedPaths.Add(fileSystem.GetFullPath(listingPath));
            try
            {
                foreach (var file in fileSystem.EnumerateFiles(listingPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    listedCount++;
                    if (listedCount % ListingProgressInterval == 0)
                    {
                        progressReporter.Report(null, $"The scan has listed {listedCount} file(s).");
                    }

                    if (!seenPaths.Add(fileSystem.GetFullPath(file)))
                    {
                        continue;
                    }

                    if (HasSampleDirectorySegment(file) ||
                        !await fileSystem
                            .MatchesAnyFileTypeAsync(file, plan.AcceptedFileTypes, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        continue;
                    }

                    planned.Add(new(plan, file, ToSignalDictionary(provider.Parse(file, out _))));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The job failed to enumerate files under {BasePath} of root folder {RootFolderId}",
                    plan.BasePath,
                    rootFolderId);
                throw;
            }
        }

        await DeleteVanishedRejectionsAsync(keptCandidates, scannedPaths, seenPaths, userId, cancellationToken)
            .ConfigureAwait(false);

        var selected = new List<PlannedFile>(planned.Count);
        foreach (var group in planned.GroupBy(file => ComputeReleaseKey(file.Plan, file.FilePath), PathComparer))
        {
            var releaseSignals = ToSignalDictionary(provider.Parse(group.Key ?? string.Empty, out _));
            var matched = group.Where(file => HasMatchingTitle(file.Signals, releaseSignals)).ToList();
            var kept = matched;
            if (kept.Count == 0)
            {
                kept = group.Where(file => HasSearchableTitle(file.Signals)).ToList();
            }

            if (kept.Count == 0)
            {
                kept = group.ToList();
            }

            foreach (var dropped in group.Where(file => !kept.Contains(file)))
            {
                logger.LogDebug(
                    "The scan skipped {FilePath} because its title does not match release {ReleaseKey}",
                    dropped.FilePath,
                    group.Key);
            }

            selected.AddRange(kept);
        }

        var total = selected.Count;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedFile = selected[i];
            var plan = selectedFile.Plan;
            var file = selectedFile.FilePath;
            progressReporter.ReportFraction(i, total, $"The scan will examine {Path.GetFileName(file)}.");

            try
            {
                await ProcessFileAsync(
                    rootFolder,
                    scanRunId,
                    file,
                    ComputeLayoutKey(plan, file),
                    selectedFile.Signals,
                    inFlightPaths,
                    linkedPaths,
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The job failed to process {FilePath} during scan {ScanRunId}", file, scanRunId);
                await TryPersistFailureAsync(
                    rootFolder,
                    scanRunId,
                    file,
                    ex,
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await RefreshKeptTargetsAsync(rootFolder, keptCandidates, userId, correlationId, cancellationToken)
            .ConfigureAwait(false);

        if (await HasUnresolvedCandidatesAsync(cancellationToken).ConfigureAwait(false))
        {
            await EnqueueMatchResolutionAsync(userId, correlationId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var summary = $"Scan of {total} file(s) complete.";
            await eventBus
                .PublishAsync(
                    ContentEventBuilder.BuildLibraryScanCompletedEvent(rootFolderId, summary) with
                    {
                        CorrelationId = correlationId
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The job failed to publish library.scan.completed for {RootFolderId}", rootFolderId);
        }

        progressReporter.Report(1, "The root-folder scan completed.");
    }

    private async Task ReclaimStrandedImportsAsync(
        Guid rootFolderId,
        DateTime cutoff,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var inFlight = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery
            {
                RootFolderId = rootFolderId,
                Statuses = [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing]
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var candidate in inFlight)
        {
            var lastActivity = candidate.ModifiedAt ?? candidate.CreatedAt;
            if (lastActivity > cutoff)
            {
                continue;
            }

            var reclaimed = await libraryImportCandidateRepository
                .TryReclaimInactiveImportAsync(candidate.Id, cutoff, userId, cancellationToken)
                .ConfigureAwait(false);
            if (reclaimed)
            {
                logger.LogWarning(
                    "The job reclaimed stranded library import candidate {LibraryImportCandidateId} " +
                    "({FilePath}), stuck in {CandidateStatus} status since {LastActivity:o}",
                    candidate.Id,
                    candidate.FilePath,
                    candidate.Status,
                    lastActivity);
            }
        }
    }

    private bool IsKept(LibraryImportCandidate candidate, IReadOnlySet<string> linkedPaths) => candidate.Status switch
    {
        LibraryImportCandidateStatus.Queued or LibraryImportCandidateStatus.Importing or
            LibraryImportCandidateStatus.Rejected => true,
        LibraryImportCandidateStatus.Pending or LibraryImportCandidateStatus.Failed =>
            candidate.Matches.Count > 0 && fileSystem.FileExists(candidate.FilePath) &&
                !linkedPaths.Contains(fileSystem.GetFullPath(candidate.FilePath)),
        _ => false
    };

    private async Task RefreshKeptTargetsAsync(
        RootFolder rootFolder,
        IReadOnlyList<LibraryImportCandidate> keptCandidates,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var matched = keptCandidates
            .Where(candidate => candidate.Status is LibraryImportCandidateStatus.Pending or
                LibraryImportCandidateStatus.Failed)
            .ToList();
        if (matched.Count == 0)
        {
            return;
        }

        try
        {
            var retargeted = await libraryImportService
                .RefreshLibraryTargetsAsync(matched, userId, cancellationToken)
                .ConfigureAwait(false);
            if (rootFolder.AutoImport && retargeted.Count > 0)
            {
                await libraryImportService
                    .AssociateMissingFilesAsync(retargeted, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The scan failed to refresh the library items of kept matches");
        }
    }

    private async Task DeleteVanishedRejectionsAsync(
        IReadOnlyList<LibraryImportCandidate> keptCandidates,
        IReadOnlyList<string> scannedPaths,
        IReadOnlySet<string> seenPaths,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in keptCandidates.Where(candidate =>
            candidate.Status == LibraryImportCandidateStatus.Rejected))
        {
            var path = fileSystem.GetFullPath(candidate.FilePath);
            if (!seenPaths.Contains(path) && scannedPaths.Any(scanned => fileSystem.IsPathWithin(scanned, path)))
            {
                await libraryImportCandidateRepository.DeleteAsync(candidate.Id, userId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlySet<string>> LoadLinkedPathsAsync(
        IEnumerable<string> basePaths,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(PathComparer);
        foreach (var basePath in basePaths)
        {
            var prefix = fileSystem.GetFullPath(basePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var linked = await mediaFileRepository.QueryAllAsync(
                new MediaFileQuery { FilePathPrefix = prefix },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var item in linked)
            {
                paths.Add(fileSystem.GetFullPath(item.FilePath));
            }
        }

        return paths;
    }

    private string? ComputeLayoutKey(ScanPlan plan, string filePath)
    {
        if (!plan.GroupingEnabled)
        {
            return null;
        }

        var relative = Path.GetRelativePath(plan.BasePath, filePath);
        var separatorIndex = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (separatorIndex <= 0)
        {
            return null;
        }

        return fileSystem.GetFullPath(Path.Combine(plan.BasePath, relative[..separatorIndex]));
    }

    private string ComputeReleaseKey(ScanPlan plan, string filePath)
    {
        var relative = Path.GetRelativePath(plan.BasePath, filePath);
        var separatorIndex = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separatorIndex <= 0
            ? fileSystem.GetFullPath(filePath)
            : fileSystem.GetFullPath(Path.Combine(plan.BasePath, relative[..separatorIndex]));
    }

    private async Task ProcessFileAsync(
        RootFolder rootFolder,
        Guid scanRunId,
        string filePath,
        string? layoutKey,
        Dictionary<string, List<string>> signals,
        IReadOnlySet<string> inFlightPaths,
        IReadOnlySet<string> linkedPaths,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (inFlightPaths.Contains(fileSystem.GetFullPath(filePath)))
        {
            return;
        }

        if (linkedPaths.Contains(fileSystem.GetFullPath(filePath)))
        {
            return;
        }

        var needsMatchResolution = layoutKey is not null || HasSearchableTitle(signals);
        var status = needsMatchResolution
            ? LibraryImportCandidateStatus.MatchingPending
            : LibraryImportCandidateStatus.Pending;
        var quarantineReason = needsMatchResolution ? null : "The filename contains no title.";

        var importCandidate = new LibraryImportCandidate
        {
            RootFolderId = rootFolder.Id,
            ScanRunId = scanRunId,
            FilePath = filePath,
            ContentType = rootFolder.ContentType,
            Status = status,
            ParsedSignals = signals,
            LayoutKey = layoutKey,
            Matches = [],
            Notes = quarantineReason
        };

        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                await libraryImportCandidateRepository.CreateAsync(importCandidate, userId, ct)
                    .ConfigureAwait(false);

                await WriteAuditAsync(
                    operation: "FileScanned",
                    entityType: nameof(LibraryImportCandidate),
                    entityId: importCandidate.Id.ToString(),
                    detail: needsMatchResolution
                        ? $"Scan queued {Path.GetFileName(filePath)} for match resolution."
                        : $"Scan held {Path.GetFileName(filePath)} for manual resolve: {quarantineReason}",
                    userId,
                    correlationId,
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasUnresolvedCandidatesAsync(CancellationToken cancellationToken)
    {
        var page = await libraryImportCandidateRepository.QueryAsync(
            new LibraryImportCandidateQuery { Status = LibraryImportCandidateStatus.MatchingPending },
            new() { PageNumber = 1, PageSize = 1 },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        return page.TotalItems > 0;
    }

    private async Task EnqueueMatchResolutionAsync(
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = MagpieJobs.Types.LibraryImportMatchResolution,
                    Mode = JobExecutionMode.Queue,
                    UserId = userId,
                    CorrelationId = correlationId,
                },
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("The scan queued a library import match resolution run");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The scan failed to queue a library import match resolution run");
        }
    }

    private async Task TryPersistFailureAsync(
        RootFolder rootFolder,
        Guid scanRunId,
        string filePath,
        Exception ex,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var failure = new LibraryImportCandidate
            {
                RootFolderId = rootFolder.Id,
                ScanRunId = scanRunId,
                FilePath = filePath,
                ContentType = rootFolder.ContentType,
                Status = LibraryImportCandidateStatus.Failed,
                Notes = $"Scan failed: {ex.Message}"
            };
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    await libraryImportCandidateRepository.CreateAsync(failure, userId, ct)
                        .ConfigureAwait(false);

                    await WriteAuditAsync(
                        operation: "FileErrored",
                        entityType: nameof(LibraryImportCandidate),
                        entityId: failure.Id.ToString(),
                        detail: $"Scan failed for {Path.GetFileName(filePath)}: {ex.Message}",
                        userId,
                        correlationId,
                        ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception writeEx)
        {
            logger.LogWarning(writeEx, "The job failed to save the failure row for {FilePath}", filePath);
        }
    }

    private async Task WriteAuditAsync(
        string operation,
        string? entityType,
        string? entityId,
        string detail,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
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
}
