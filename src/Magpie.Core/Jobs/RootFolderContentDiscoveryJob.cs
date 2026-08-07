using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RootFolderContentDiscoveryJob(
    IRootFolderRepository rootFolderRepository,
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IMediaFileRepository mediaFileRepository,
    IAuditRecordRepository auditRecordRepository,
    IEnumerable<IContentTypeDefinitionProvider> contentTypeDefinitionProviders,
    IMetadataSearchService metadataSearchService,
    IFileSystem fileSystem,
    IEventBus eventBus,
    IProgressReporter progressReporter,
    TimeProvider timeProvider,
    ILogger<RootFolderContentDiscoveryJob> logger) : IJob
{
    private static readonly Regex TrailingYearToken = new(
        @"\s+(19|20)\d{2}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordPattern = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> IgnoredTitleTokens =
    [
        "480p", "540p", "576p", "720p", "1080p", "1080i", "2160p", "4320p", "4k", "8k", "uhd", "hd", "sd",
        "bluray", "bdrip", "brrip", "bdremux", "remux", "web", "webdl", "webrip", "hdrip", "dvdrip", "dvd",
        "hdtv", "pdtv", "sdtv", "cam", "ts", "tc", "telesync", "screener", "scr", "hdcam", "hdts",
        "x264", "x265", "h264", "h265", "hevc", "avc", "av1", "xvid", "divx", "vp9", "10bit", "8bit",
        "hdr", "hdr10", "dv", "dolbyvision", "sdr",
        "aac", "ac3", "eac3", "dts", "dtshd", "truehd", "atmos", "flac", "mp3", "opus", "ddp", "dd5", "51", "71",
        "mkv", "mp4", "avi", "x26", "hi10p", "dl",
        "extended", "unrated", "uncut", "directors", "director", "theatrical", "remastered", "remaster",
        "proper", "repack", "rerip", "internal", "limited", "complete", "final", "cut", "edition",
        "imax", "criterion", "anniversary", "special", "collectors", "restored", "open", "matte",
        "multi", "dual", "audio", "subs", "subbed", "dubbed", "vostfr", "ita", "eng", "esp", "ger", "fre",
    ];

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "a", "an", "of", "to", "in", "on", "at", "by", "for", "from", "with", "as",
        "or", "nor", "but", "so", "yet", "vs", "vs.", "versus", "is", "it", "its", "be", "are",
        "this", "that", "these", "those", "into", "onto", "over", "under", "up", "us", "we", "our",
    ];

    public static readonly JobSpec Spec = new()
    {
        JobType = JobTypeName,
        DisplayName = "Root folder content discovery",
        Description = "Discovers content in a root folder and creates library import candidates.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(30),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [RootFolderIdParameter],
        LockPartitionEventContextKeys = new Dictionary<string, string>
        {
            [RootFolderIdParameter] = MagpieEvents.Keys.RootFolderId
        },
        Parameters = [new(RootFolderIdParameter, "Required. The root folder to scan.", DataType.String, true, null)],
    };

    private const string JobTypeName = "RootFolderContentDiscovery";

    private const string RootFolderIdParameter = "rootFolderId";

    private const double MatchConfidenceTieTolerance = 0.000001d;

    private const double MinimumTitleCoverage = 0.75d;

    private static readonly TimeSpan StrandedImportReclaimThreshold = TimeSpan.FromHours(1);

    private enum RootFolderIdSource
    {
        Parameter,
        EventContext,
    }

    private static IReadOnlyList<string> GetSearchQueries(
        IReadOnlyDictionary<string, IReadOnlyList<string>> signals)
    {
        var candidates = signals.GetValues(ParsedFileSignalKeys.TitleToken);
        if (candidates.Count == 0)
        {
            candidates = signals.GetValues(ParsedFileSignalKeys.Title);
        }

        return [.. candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static IReadOnlyList<MetadataSearchCandidate> SelectLevelResults(
        IReadOnlyList<MetadataSearchCandidate> results,
        string? levelSlug)
    {
        return string.IsNullOrWhiteSpace(levelSlug)
            ? results
            : [.. results.Where(result => string.Equals(result.Level, levelSlug, StringComparison.Ordinal))];
    }

    private static Dictionary<string, List<string>> ToSignalDictionary(
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

    private static bool TryStripTrailingYear(string title, out string strippedTitle)
    {
        strippedTitle = title;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var match = TrailingYearToken.Match(title);
        if (!match.Success)
        {
            return false;
        }

        var candidate = title[..match.Index].TrimEnd();
        if (candidate.Length == 0)
        {
            return false;
        }

        strippedTitle = candidate;
        return true;
    }

    private static LibraryImportMatch MapMatch(MetadataSearchCandidate item, double confidence)
    {
        var year = item.Year ?? item.ReleaseDate?.Year;
        return new LibraryImportMatch
        {
            Confidence = confidence,
            ProviderType = item.ProviderType,
            ExternalIdKey = item.ExternalIdSource,
            ExternalId = item.ExternalId,
            Title = item.Title ?? string.Empty,
            Subtitle = year?.ToString(CultureInfo.InvariantCulture),
            ImageUrl = item.ThumbnailUrl?.ToString(),
            LevelSlug = item.Level
        };
    }

    private static bool TryResolveRootFolderId(JobContext context, out string raw, out RootFolderIdSource source)
    {
        if (context.Parameters.TryGetValue(RootFolderIdParameter, out var rawParameter) &&
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

    private static IReadOnlyList<string> GetTitleCandidates(
        IReadOnlyDictionary<string, IReadOnlyList<string>> signals)
    {
        return [.. signals.GetValues(ParsedFileSignalKeys.TitleToken)
            .Concat(signals.GetValues(ParsedFileSignalKeys.Title))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool AreCompactTitlesEqual(string? fileTitle, string? canonicalTitle)
    {
        var fileCompact = NormalizeCompact(fileTitle);
        if (fileCompact.Length == 0)
        {
            return false;
        }

        return string.Equals(fileCompact, NormalizeCompact(canonicalTitle), StringComparison.Ordinal);
    }

    private static string NormalizeCompact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static HashSet<string> RemoveFileReleaseYearTokens(
        HashSet<string> fileTokens,
        HashSet<string> canonicalTokens,
        int? queryYear)
    {
        var comparableTokens = new HashSet<string>(fileTokens);
        if (queryYear is { } year)
        {
            comparableTokens.Remove(year.ToString(CultureInfo.InvariantCulture));
        }

        if (comparableTokens.Count <= 1)
        {
            return comparableTokens;
        }

        foreach (var token in fileTokens)
        {
            if (!canonicalTokens.Contains(token) && IsLikelyYearToken(token))
            {
                comparableTokens.Remove(token);
            }
        }

        return comparableTokens;
    }

    private static bool IsLikelyYearToken(string token) =>
        token.Length == 4 &&
        token[0] is '1' or '2' &&
        token[1] is '9' or '0' &&
        char.IsDigit(token[2]) &&
        char.IsDigit(token[3]);

    private static HashSet<string> Tokenize(string? value)
    {
        var tokens = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return tokens;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    private static bool IsTitleMatch(
        string? fileTitle,
        string? canonicalTitle,
        HashSet<string> fileTokens,
        HashSet<string> canonicalTokens)
    {
        if (AreCompactTitlesEqual(fileTitle, canonicalTitle))
        {
            return true;
        }

        if (canonicalTokens.Count == 0)
        {
            return true;
        }

        if (fileTokens.Count == 0)
        {
            return false;
        }

        var canonicalContentTokens = ContentTokens(canonicalTokens);
        var fileContentTokens = ContentTokens(fileTokens);
        var canonicalCoverage = CalculateTitleCoverage(canonicalContentTokens, new HashSet<string>(fileContentTokens));
        var fileCoverage = CalculateTitleCoverage(fileContentTokens, new HashSet<string>(canonicalContentTokens));
        return canonicalCoverage >= MinimumTitleCoverage && fileCoverage >= MinimumTitleCoverage;
    }

    private static IReadOnlyList<string> ContentTokens(IEnumerable<string> tokens) =>
        SignificantTitleWords(string.Join(' ', tokens));

    private static IReadOnlyList<string> SignificantTitleWords(string? title) =>
        [.. MatchWords(title).Where(word => !IsYear(word))];

    private static IReadOnlyList<string> MatchWords(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var words = new List<string>();
        foreach (Match match in WordPattern.Matches(title))
        {
            var text = match.Value;
            if (IgnoredTitleTokens.Contains(text.ToLowerInvariant()))
            {
                continue;
            }

            var lower = text.ToLowerInvariant();
            if (!StopWords.Contains(lower))
            {
                words.Add(lower);
            }
        }

        return words;
    }

    private static bool IsYear(string text) =>
        text.Length == 4 && int.TryParse(text, out var year) && year is >= 1900 and <= 2099;

    private static double CalculateTitleCoverage(IReadOnlyList<string> tokens, ISet<string> coveredBy)
    {
        var covered = 0d;
        var total = 0d;
        foreach (var token in tokens)
        {
            total += token.Length;
            if (coveredBy.Contains(token))
            {
                covered += token.Length;
            }
        }

        return total == 0 ? 1d : covered / total;
    }

    private static List<ScanPlan> BuildRootLevelScanPlans(
        RootFolder rootFolder,
        ContentTypeDefinition definition)
    {
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
                plans.Add(new ScanPlan(basePath, accepted));
            }
        }

        return plans;
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

    private double Score(IReadOnlyDictionary<string, IReadOnlyList<string>> signals, MetadataSearchCandidate result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var titleCandidates = GetTitleCandidates(signals);
        if (titleCandidates.Count == 0)
        {
            return ReleaseMatchConfidence.None;
        }

        var confidence = ReleaseMatchConfidence.None;
        foreach (var title in titleCandidates)
        {
            confidence = Math.Max(confidence, ScoreTitle(title, signals, result));
        }

        return confidence;
    }

    private double ScoreTitle(
        string title,
        IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        MetadataSearchCandidate result)
    {
        var fileTokens = Tokenize(title);
        var canonicalTokens = Tokenize(result.Title);

        var queryYear = signals.GetFirstInt32(ParsedFileSignalKeys.Year);
        fileTokens = RemoveFileReleaseYearTokens(fileTokens, canonicalTokens, queryYear);

        if (!IsTitleMatch(title, result.Title, fileTokens, canonicalTokens))
        {
            return ReleaseMatchConfidence.None;
        }

        var confidence = ReleaseMatchConfidence.TitleWeight;

        var resultYear = result.Year ?? result.ReleaseDate?.Year;
        if (queryYear is { } fileYear && resultYear is { } canonicalYear)
        {
            if (fileYear == canonicalYear)
            {
                confidence += ReleaseMatchConfidence.YearWeight;
            }
        }

        return Math.Min(confidence, ReleaseMatchConfidence.Certain);
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!TryResolveRootFolderId(context, out var raw, out var source))
        {
            throw new ArgumentException("A root folder ID is required.");
        }

        if (!Guid.TryParse(raw, out var rootFolderId))
        {
            throw new ArgumentException(
                source is RootFolderIdSource.EventContext
                    ? "The job received an invalid root folder ID."
                    : "The root folder ID has an invalid format.");
        }

        await ScanAsync(
            rootFolderId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ScanAsync(
        Guid rootFolderId,
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

        var provider = ResolveProvider(rootFolder.ContentType);
        if (provider is null)
        {
            logger.LogWarning("No definition provider exists for {ContentType}. No scan ran", rootFolder.ContentType);
            progressReporter.Report(1, "The scan stopped because the content type has no definition provider.");
            return;
        }

        var definition = provider.Definition;
        var plans = BuildRootLevelScanPlans(rootFolder, definition);
        if (plans.Count == 0)
        {
            logger.LogWarning("{ContentType} has no accepted file types. No scan ran", rootFolder.ContentType);
            progressReporter.Report(1, "The scan stopped because the content type accepts no file types.");
            return;
        }

        var scanRunId = Guid.NewGuid();
        logger.LogInformation(
            "The job will start scan {ScanRunId} of root folder {RootFolderId}",
            scanRunId,
            rootFolderId);

        await ReclaimStrandedImportsAsync(rootFolderId, userId, cancellationToken).ConfigureAwait(false);

        await libraryImportCandidateRepository
            .DeleteByRootFolderAsync(rootFolderId, userId, cancellationToken)
            .ConfigureAwait(false);

        var inFlightPaths = await LoadInFlightPathsAsync(rootFolderId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.Report(null, "The scan will list the files.");

        var planned = new List<(ScanPlan Plan, string FilePath)>();
        var seenPaths = new List<string>();
        foreach (var plan in plans)
        {
            if (!fileSystem.DirectoryExists(plan.BasePath))
            {
                logger.LogInformation(
                    "Root folder {RootFolderId} has no folder at {BasePath}, so the scan skipped it",
                    rootFolderId,
                    plan.BasePath);
                continue;
            }

            try
            {
                foreach (var file in fileSystem.EnumerateFiles(plan.BasePath))
                {
                    if (seenPaths.Any(seen => fileSystem.PathsEqual(seen, file)))
                    {
                        continue;
                    }

                    seenPaths.Add(file);
                    planned.Add((plan, file));
                }
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

        var total = planned.Count;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (plan, file) = planned[i];
            progressReporter.ReportFraction(i, total, $"The scan will examine {Path.GetFileName(file)}.");

            if (!await fileSystem
                    .MatchesAnyFileTypeAsync(file, plan.AcceptedFileTypes, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                await ProcessFileAsync(
                    rootFolder,
                    scanRunId,
                    file,
                    provider,
                    definition,
                    inFlightPaths,
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

        try
        {
            var summary = $"Scan of {total} file(s) complete.";
            await eventBus
                .PublishAsync(
                    ContentEventBuilder.BuildLibraryScanCompletedEvent(rootFolderId, summary),
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

    private IContentTypeDefinitionProvider? ResolveProvider(string contentType)
    {
        foreach (var candidate in contentTypeDefinitionProviders)
        {
            ContentTypeDefinition definition;
            try
            {
                definition = candidate.Definition;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Provider {Provider} failed to read a definition", candidate.GetType().FullName);
                continue;
            }

            if (definition is null)
            {
                continue;
            }

            if (definition.Slug == contentType)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task ReclaimStrandedImportsAsync(Guid rootFolderId, Guid? userId, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var inFlight = new List<LibraryImportCandidate>();
        var pageNumber = 1;
        while (true)
        {
            var page = await libraryImportCandidateRepository.QueryAsync(
                new LibraryImportCandidateQuery
                {
                    RootFolderId = rootFolderId,
                    Statuses = [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing]
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            inFlight.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - StrandedImportReclaimThreshold;
        foreach (var candidate in inFlight)
        {
            var lastActivity = candidate.ModifiedAt ?? candidate.CreatedAt;
            if (lastActivity > cutoff)
            {
                continue;
            }

            var reclaimed = await libraryImportCandidateRepository
                .MarkImportFailedAsync(
                    candidate.Id,
                    "Import did not complete within the reclaim window and was reset so the file can be rescanned.",
                    userId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (reclaimed)
            {
                logger.LogWarning(
                    "The job reclaimed stranded library import candidate {LibraryImportCandidateId} ({FilePath}). " +
                    "It was stuck in {CandidateStatus} since {LastActivity:o}",
                    candidate.Id,
                    candidate.FilePath,
                    candidate.Status,
                    lastActivity);
            }
        }
    }

    private async Task<IReadOnlyList<string>> LoadInFlightPathsAsync(
        Guid rootFolderId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var paths = new List<string>();
        var pageNumber = 1;
        while (true)
        {
            var page = await libraryImportCandidateRepository.QueryAsync(
                new LibraryImportCandidateQuery
                {
                    RootFolderId = rootFolderId,
                    Statuses = [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing]
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            foreach (var path in pageItems.Select(candidate => candidate.FilePath))
            {
                if (!paths.Any(existing => fileSystem.PathsEqual(existing, path)))
                {
                    paths.Add(path);
                }
            }

            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        return paths;
    }

    private async Task ProcessFileAsync(
        RootFolder rootFolder,
        Guid scanRunId,
        string filePath,
        IContentTypeDefinitionProvider provider,
        ContentTypeDefinition definition,
        IReadOnlyList<string> inFlightPaths,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (inFlightPaths.Any(path => fileSystem.PathsEqual(path, filePath)))
        {
            return;
        }

        var alreadyLinked = (await mediaFileRepository
            .QueryAsync(
                new() { FilePaths = [filePath] },
                new() { PageNumber = 1, PageSize = 1 },
                null,
                cancellationToken)
            .ConfigureAwait(false)).TotalItems > 0;
        if (alreadyLinked)
        {
            return;
        }

        var parsed = provider.Parse(filePath, out var levelSlug);

        List<LibraryImportMatch> matches;
        string? quarantineReason;
        if (parsed is { } query)
        {
            (matches, quarantineReason) = await ResolveConfidentMatchAsync(
                query, levelSlug, rootFolder.ContentType, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            matches = [];
            quarantineReason = "The filename contains no title.";
        }

        if (matches is [{ LevelSlug: { } targetLevelSlug }])
        {
            var targetLevel = definition.LevelDefinitions.FirstOrDefault(level => level.Slug == targetLevelSlug);
            if (targetLevel is null)
            {
                logger.LogDebug(
                    "The job skipped {FilePath} because matched target level {TargetLevelSlug} is not declared",
                    filePath,
                    targetLevelSlug);
                return;
            }

            if (!await fileSystem
                    .MatchesAnyFileTypeAsync(filePath, targetLevel.AcceptedFileTypes, cancellationToken)
                    .ConfigureAwait(false))
            {
                logger.LogDebug(
                    "The job skipped {FilePath} because target level {TargetLevelSlug} does not accept its file type",
                    filePath,
                    targetLevelSlug);
                return;
            }
        }

        var importCandidate = new LibraryImportCandidate
        {
            RootFolderId = rootFolder.Id,
            ScanRunId = scanRunId,
            FilePath = filePath,
            ContentType = rootFolder.ContentType,
            Status = LibraryImportCandidateStatus.Pending,
            ParsedSignals = ToSignalDictionary(parsed),
            Matches = matches,
            Notes = quarantineReason
        };

        await libraryImportCandidateRepository.CreateAsync(importCandidate, userId, cancellationToken)
            .ConfigureAwait(false);

        await TryWriteAuditAsync(
            operation: "FileScanned",
            entityType: nameof(LibraryImportCandidate),
            entityId: importCandidate.Id.ToString(),
            detail: matches.Count == 1
                ? $"Scan confidently matched {Path.GetFileName(filePath)} to a single import match."
                : $"Scan held {Path.GetFileName(filePath)} for manual resolve: {quarantineReason}",
            userId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(List<LibraryImportMatch> Matches, string? QuarantineReason)> ResolveConfidentMatchAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        string? levelSlug,
        string contentType,
        CancellationToken cancellationToken)
    {
        var searchQueries = GetSearchQueries(signals);
        if (searchQueries.Count == 0)
        {
            return ([], "The filename contains no title.");
        }

        var searchedTitle = searchQueries[0];
        var searchResults = (IReadOnlyList<MetadataSearchCandidate>)[];
        foreach (var searchQuery in searchQueries)
        {
            searchedTitle = searchQuery;
            searchResults = await metadataSearchService
                .SearchAsync(searchQuery, contentType, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            searchResults = SelectLevelResults(searchResults, levelSlug);

            if (searchResults.Count == 0 && TryStripTrailingYear(searchQuery, out var bareTitle))
            {
                searchResults = await metadataSearchService
                    .SearchAsync(bareTitle, contentType, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                searchResults = SelectLevelResults(searchResults, levelSlug);
                if (searchResults.Count > 0)
                {
                    searchedTitle = bareTitle;
                }
            }

            if (searchResults.Count > 0)
            {
                break;
            }
        }

        if (searchResults.Count == 0)
        {
            return ([], $"No metadata result matched {searchQueries[0]}.");
        }

        var scored = searchResults
            .Select(result => (Item: result, Confidence: Score(signals, result)))
            .OrderByDescending(scoredResult => scoredResult.Confidence)
            .ToList();

        var confident = scored
            .Where(scoredResult => scoredResult.Confidence >= ReleaseMatchConfidence.AcceptThreshold)
            .ToList();

        if (confident.Count == 0)
        {
            var nearest = scored[0].Item.Title ?? searchedTitle;
            return (
                [],
                $"The closest result, {nearest}, scored below the match threshold.");
        }

        var best = confident[0];
        var tied = confident
            .Where(scoredResult => Math.Abs(scoredResult.Confidence - best.Confidence) <= MatchConfidenceTieTolerance)
            .ToList();

        if (tied.Count > 1)
        {
            return (
                [],
                $"{tied.Count} results have similar scores. Select a result manually.");
        }

        return ([MapMatch(best.Item, best.Confidence)], null);
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
        Guid? failureRowId = null;
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
            await libraryImportCandidateRepository.CreateAsync(failure, userId, cancellationToken)
                .ConfigureAwait(false);
            failureRowId = failure.Id;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception writeEx)
        {
            logger.LogWarning(writeEx, "The job failed to save the failure row for {FilePath}", filePath);
        }

        await TryWriteAuditAsync(
            operation: "FileErrored",
            entityType: failureRowId is null ? null : nameof(LibraryImportCandidate),
            entityId: failureRowId?.ToString(),
            detail: $"Scan failed for {Path.GetFileName(filePath)}: {ex.Message}",
            userId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
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
            logger.LogWarning(ex, "The job failed to save the audit row {EntityType}/{EntityId}", entityType, entityId);
        }
    }

    private sealed record ScanPlan(string BasePath, IReadOnlyList<string> AcceptedFileTypes);
}
