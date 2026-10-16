using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class LibraryImportMatchResolutionJob(
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IRootFolderRepository rootFolderRepository,
    ILibraryImportService libraryImportService,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IContentTypeRegistry contentTypeRegistry,
    IMetadataSearchService metadataSearchService,
    IJobManager jobManager,
    TimeProvider timeProvider,
    ILogger<LibraryImportMatchResolutionJob> logger,
    IProgressReporter progressReporter) : IJob
{
    private static readonly Regex TrailingYearToken = new(
        @"\s+\(?(?<year>(19|20)\d{2})\)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordPattern = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "a", "an", "of", "to", "in", "on", "at", "by", "for", "from", "with", "as",
        "or", "nor", "but", "so", "yet", "vs", "vs.", "versus", "is", "it", "its", "be", "are",
        "this", "that", "these", "those", "into", "onto", "over", "under", "up", "us", "we", "our",
    ];

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.LibraryImportMatchResolution,
        DisplayName = "Library import match resolution",
        Description = "Finds metadata matches for files found in root folders.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private const double MatchConfidenceTieTolerance = 0.000001d;

    private const double MinimumTitleCoverage = 0.75d;

    private const int MaximumFailureNoteLength = 512;

    private static readonly TimeSpan ProviderRetryDelay = TimeSpan.FromMinutes(1);

    private static IReadOnlyList<string> GetSearchQueries(IReadOnlyDictionary<string, IReadOnlyList<string>> signals)
    {
        var candidates = signals.GetValues(ParsedFileSignalKeys.RootTitle);
        if (candidates.Count == 0)
        {
            candidates = signals.GetValues(ParsedFileSignalKeys.TitleToken);
        }

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

    private static IReadOnlyList<string> GetTitleCandidates(IReadOnlyDictionary<string, IReadOnlyList<string>> signals)
    {
        var rootTitles = signals.GetValues(ParsedFileSignalKeys.RootTitle)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (rootTitles.Count > 0)
        {
            return rootTitles;
        }

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
            if (ch == '&')
            {
                builder.Append("and");
            }
            else if (char.IsLetterOrDigit(ch))
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
            if (!canonicalTokens.Contains(token) && IsYear(token))
            {
                comparableTokens.Remove(token);
            }
        }

        return comparableTokens;
    }

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

        var canonicalContentTokens = ContentTokens(canonicalTokens);
        var fileContentTokens = ContentTokens(fileTokens);
        if (canonicalContentTokens.Count == 0 || fileContentTokens.Count == 0)
        {
            return false;
        }

        var canonicalCoverage = CalculateTitleCoverage(canonicalContentTokens, new HashSet<string>(fileContentTokens));
        var fileCoverage = CalculateTitleCoverage(fileContentTokens, new HashSet<string>(canonicalContentTokens));
        return canonicalCoverage >= MinimumTitleCoverage && fileCoverage >= MinimumTitleCoverage;
    }

    private static IReadOnlyList<string> ContentTokens(IEnumerable<string> tokens)
    {
        var words = tokens.Where(token => !IsYear(token)).ToList();
        var significant = words.Where(word => !StopWords.Contains(word)).ToList();

        return significant.Count > 0 ? significant : words;
    }

    private static IReadOnlyList<string> MatchWords(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var words = new List<string>();
        foreach (Match match in WordPattern.Matches(title))
        {
            words.Add(match.Value.ToLowerInvariant());
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

    private static double Score(
        IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        MetadataSearchCandidate result)
    {
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

    private static bool IsExactTitleMatch(
        IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        MetadataSearchCandidate result)
    {
        var queryYear = signals.GetFirstInt32(ParsedFileSignalKeys.Year);
        foreach (var title in GetTitleCandidates(signals))
        {
            if (AreCompactTitlesEqual(title, result.Title))
            {
                return true;
            }

            var match = TrailingYearToken.Match(title);
            if (queryYear is not { } year || !match.Success ||
                match.Groups["year"].Value != year.ToString(CultureInfo.InvariantCulture) ||
                !TryStripTrailingYear(title, out var titleWithoutYear))
            {
                continue;
            }

            if (AreCompactTitlesEqual(titleWithoutYear, result.Title))
            {
                return true;
            }
        }

        return false;
    }

    private static double ScoreTitle(
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
        var userId = context.ManualTriggerUserId;
        var correlationId = context.CorrelationId;

        progressReporter.Report(0, "The match resolution will load unresolved candidates.");

        var batch = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery { Status = LibraryImportCandidateStatus.MatchingPending },
            new() { PropertyName = nameof(LibraryImportCandidate.CreatedAt), Direction = SortDirection.Ascending },
            cancellationToken).ConfigureAwait(false);

        var searchCache = new Dictionary<(string ContentType, string Query), MetadataSearchPage>();
        var total = batch.Count;
        var resolved = 0;
        var retired = 0;
        var completedResolution = false;
        var retryRequired = false;
        var processed = 0;

        foreach (var group in batch.Where(candidate => candidate.LayoutKey is not null)
            .GroupBy(candidate => candidate.LayoutKey!))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = group.ToList();
            progressReporter.ReportFraction(
                processed,
                total,
                $"The match resolution will resolve {Path.GetFileName(group.Key)}.");

            try
            {
                if (await ResolveGroupAsync(group.Key, members, searchCache, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false) is { } count)
                {
                    resolved += count;
                    completedResolution = true;
                }
                else
                {
                    retryRequired = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The match resolution failed to resolve library import group {LayoutKey}",
                    group.Key);
                foreach (var member in members)
                {
                    if (await TryPersistResolutionFailureAsync(
                        member,
                        ex,
                        userId,
                        correlationId,
                        cancellationToken).ConfigureAwait(false) is { } marked)
                    {
                        retired += marked ? 1 : 0;
                        completedResolution = true;
                    }
                }
            }

            processed += members.Count;
        }

        foreach (var candidate in batch.Where(candidate => candidate.LayoutKey is null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            progressReporter.ReportFraction(
                processed,
                total,
                $"The match resolution will resolve {Path.GetFileName(candidate.FilePath)}.");

            try
            {
                if (await ResolveCandidateAsync(candidate, searchCache, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false) is { } count)
                {
                    resolved += count;
                    completedResolution = true;
                }
                else
                {
                    retryRequired = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The match resolution failed to resolve library import candidate {LibraryImportCandidateId}",
                    candidate.Id);
                if (await TryPersistResolutionFailureAsync(
                    candidate,
                    ex,
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false) is { } marked)
                {
                    retired += marked ? 1 : 0;
                    completedResolution = true;
                }
            }

            processed++;
        }

        await AutoAssociateAsync(batch, userId, correlationId, cancellationToken).ConfigureAwait(false);

        var progressed = batch.Count == 0 || completedResolution || retired > 0;
        if (progressed || retryRequired)
        {
            if (await HasUnresolvedCandidatesAsync(cancellationToken).ConfigureAwait(false))
            {
                await RequeueSelfAsync(userId, correlationId, retryRequired, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogWarning(
                "The match resolution moved no candidate out of the pending state and stopped requeueing " +
                "itself, leaving {Count} candidate(s) unresolved until the next scan",
                batch.Count);
        }

        progressReporter.Report(1, $"The match resolution completed. Resolved {resolved} of {total} candidate(s).");
    }

    private async Task AutoAssociateAsync(
        IReadOnlyList<LibraryImportCandidate> batch,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var targeted = batch
            .Where(candidate => candidate.Matches.Count == 1 && candidate.ContentHierarchyItemId is not null)
            .ToList();
        var autoImportRootFolderIds = new HashSet<Guid>();
        foreach (var rootFolderId in targeted.Select(candidate => candidate.RootFolderId).Distinct())
        {
            var rootFolder = await rootFolderRepository.GetByIdAsync(rootFolderId, cancellationToken)
                .ConfigureAwait(false);
            if (rootFolder?.AutoImport == true)
            {
                autoImportRootFolderIds.Add(rootFolderId);
            }
        }

        var candidates = targeted
            .Where(candidate => autoImportRootFolderIds.Contains(candidate.RootFolderId))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        try
        {
            var result = await libraryImportService
                .AssociateMissingFilesAsync(candidates, userId, correlationId, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "The match resolution queued {Queued} automatic association(s) and skipped {Skipped}",
                result.Queued.Count,
                result.Skipped.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The match resolution failed to queue automatic associations");
        }
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

    private async Task RequeueSelfAsync(
        Guid? userId,
        Guid? correlationId,
        bool retryRequired,
        CancellationToken cancellationToken)
    {
        try
        {
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = Spec.JobType,
                    Mode = JobExecutionMode.Queue,
                    UserId = userId,
                    CorrelationId = correlationId,
                    IsSuccessor = true,
                    NotBeforeAt = retryRequired
                        ? timeProvider.GetUtcNow().UtcDateTime.Add(ProviderRetryDelay)
                        : null,
                },
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("The match resolution requeued itself because unresolved candidates remain");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The match resolution failed to requeue itself for the remaining candidates");
        }
    }

    private async Task<int?> ResolveCandidateAsync(
        LibraryImportCandidate candidate,
        Dictionary<(string ContentType, string Query), MetadataSearchPage> searchCache,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var provider = contentTypeRegistry.FindProviderFor(candidate.ContentType);
        if (provider is null)
        {
            candidate.Status = LibraryImportCandidateStatus.Pending;
            candidate.Notes = $"No definition provider exists for content type {candidate.ContentType}.";
            return await libraryImportCandidateRepository.TryResolveMatchAsync(candidate, userId, cancellationToken)
                .ConfigureAwait(false) ? 1 : 0;
        }

        var parsed = provider.Parse(candidate.FilePath, out var levelSlug);

        List<LibraryImportMatch>? matches;
        string? notes;
        if (parsed is { } query)
        {
            (matches, notes) = await ResolveConfidentMatchAsync(
                query,
                levelSlug,
                candidate.ContentType,
                searchCache,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            matches = [];
            notes = "The filename contains no title.";
        }

        if (matches is null)
        {
            await SaveNotesAsync(candidate, notes, userId, cancellationToken).ConfigureAwait(false);
            return null;
        }

        candidate.Status = LibraryImportCandidateStatus.Pending;
        candidate.Matches = matches;
        candidate.Notes = notes;
        candidate.ContentHierarchyItemId = matches.Count == 1
            ? await libraryImportService.ResolveLibraryTargetAsync(candidate, cancellationToken).ConfigureAwait(false)
            : null;
        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                if (!await libraryImportCandidateRepository.TryResolveMatchAsync(candidate, userId, ct)
                    .ConfigureAwait(false))
                {
                    return 0;
                }

                await WriteAuditAsync(
                    operation: "FileMatched",
                    entityType: nameof(LibraryImportCandidate),
                    entityId: candidate.Id.ToString(),
                    detail: matches.Count == 1
                        ? $"Match resolution confidently matched {Path.GetFileName(candidate.FilePath)} to a single " +
                            "import match."
                        : $"Match resolution held {Path.GetFileName(candidate.FilePath)} for manual resolve: " +
                            $"{notes}",
                    userId,
                    correlationId,
                    ct).ConfigureAwait(false);
                return 1;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<int?> ResolveGroupAsync(
        string groupKey,
        List<LibraryImportCandidate> members,
        Dictionary<(string ContentType, string Query), MetadataSearchPage> searchCache,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var contentType = members[0].ContentType;
        var provider = contentTypeRegistry.FindProviderFor(contentType);

        List<LibraryImportMatch>? matches;
        string? notes;
        if (provider is null)
        {
            matches = [];
            notes = $"No definition provider exists for content type {contentType}.";
        }
        else
        {
            var rootLevelSlug = provider.Definition.LevelDefinitions
                .FirstOrDefault(level => level.ParentSlug is null)?.Slug;
            var directoryName = Path.GetFileName(groupKey);
            var signals = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [ParsedFileSignalKeys.RootTitle] = [directoryName]
            };
            var yearMatch = TrailingYearToken.Match(directoryName);
            if (yearMatch.Success)
            {
                signals[ParsedFileSignalKeys.Year] = [yearMatch.Groups["year"].Value];
            }

            (matches, notes) = await ResolveConfidentMatchAsync(
                signals,
                rootLevelSlug,
                contentType,
                searchCache,
                cancellationToken).ConfigureAwait(false);
        }

        if (matches is null)
        {
            foreach (var member in members)
            {
                await SaveNotesAsync(member, notes, userId, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        foreach (var candidate in members)
        {
            candidate.Status = LibraryImportCandidateStatus.Pending;
            candidate.Matches = [.. matches];
            candidate.Notes = notes;
            candidate.ContentHierarchyItemId = matches.Count == 1
                ? await libraryImportService.ResolveLibraryTargetAsync(candidate, cancellationToken)
                    .ConfigureAwait(false)
                : null;
        }

        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var resolved = 0;
                foreach (var candidate in members)
                {
                    if (!await libraryImportCandidateRepository.TryResolveMatchAsync(candidate, userId, ct)
                        .ConfigureAwait(false))
                    {
                        continue;
                    }

                    resolved++;

                    await WriteAuditAsync(
                        operation: "FileMatched",
                        entityType: nameof(LibraryImportCandidate),
                        entityId: candidate.Id.ToString(),
                        detail: matches.Count == 1
                            ? $"Match resolution confidently matched {Path.GetFileName(candidate.FilePath)} " +
                                "to a single import match."
                            : $"Match resolution held {Path.GetFileName(candidate.FilePath)} for manual resolve: " +
                                $"{notes}",
                        userId,
                        correlationId,
                        ct).ConfigureAwait(false);
                }

                return resolved;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveNotesAsync(
        LibraryImportCandidate candidate,
        string? notes,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (candidate.Notes == notes)
        {
            return;
        }

        candidate.Notes = notes;
        await libraryImportCandidateRepository.TryResolveMatchAsync(candidate, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(List<LibraryImportMatch>? Matches, string? Notes)> ResolveConfidentMatchAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        string? levelSlug,
        string contentType,
        Dictionary<(string ContentType, string Query), MetadataSearchPage> searchCache,
        CancellationToken cancellationToken)
    {
        var searchQueries = GetSearchQueries(signals);
        if (searchQueries.Count == 0)
        {
            return ([], "The filename contains no title.");
        }

        var searchedTitle = searchQueries[0];
        var searchResults = (IReadOnlyList<MetadataSearchCandidate>)[];
        var providerFailures = new List<string>();
        foreach (var searchQuery in searchQueries)
        {
            searchedTitle = searchQuery;
            var page = await SearchCachedAsync(searchQuery, contentType, searchCache, cancellationToken)
                .ConfigureAwait(false);
            providerFailures.AddRange(page.Warnings);
            searchResults = SelectLevelResults(page.Matches, levelSlug);

            if (searchResults.Count == 0 && TryStripTrailingYear(searchQuery, out var bareTitle))
            {
                var barePage = await SearchCachedAsync(bareTitle, contentType, searchCache, cancellationToken)
                    .ConfigureAwait(false);
                providerFailures.AddRange(barePage.Warnings);
                searchResults = SelectLevelResults(barePage.Matches, levelSlug);
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
            if (providerFailures.Count > 0)
            {
                var warnings = string.Join(" ", providerFailures.Distinct(StringComparer.Ordinal));
                logger.LogWarning(
                    "The metadata search for {Query} did not complete, so matching stays pending for" +
                    " retry: {Warnings}",
                    searchQueries[0],
                    warnings);
                return (null, $"Matching will retry because the metadata search did not complete: {warnings}");
            }

            return ([], $"No metadata result matched {searchQueries[0]}.");
        }

        var scored = searchResults
            .Select(result => (
                Item: result,
                Confidence: Score(signals, result),
                ExactTitleMatch: IsExactTitleMatch(signals, result)))
            .OrderByDescending(scoredResult => scoredResult.ExactTitleMatch)
            .ThenByDescending(scoredResult => scoredResult.Confidence)
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
            .Where(scoredResult => scoredResult.ExactTitleMatch == best.ExactTitleMatch &&
                Math.Abs(scoredResult.Confidence - best.Confidence) <= MatchConfidenceTieTolerance)
            .ToList();

        if (tied.Count > 1)
        {
            return (
                [],
                $"{tied.Count} results have similar scores. Select a result manually.");
        }

        return ([MapMatch(best.Item, best.Confidence)], null);
    }

    private async Task<MetadataSearchPage> SearchCachedAsync(
        string query,
        string contentType,
        Dictionary<(string ContentType, string Query), MetadataSearchPage> cache,
        CancellationToken cancellationToken)
    {
        var key = (contentType, query);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var results = await metadataSearchService
            .SearchAsync(query, contentType, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        cache[key] = results;
        return results;
    }

    private async Task<bool?> TryPersistResolutionFailureAsync(
        LibraryImportCandidate candidate,
        Exception ex,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        bool? persisted = null;
        var reason = $"Match resolution failed: {ex.Message}";
        if (reason.Length > MaximumFailureNoteLength)
        {
            reason = reason[..MaximumFailureNoteLength];
        }

        try
        {
            persisted = await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var marked = await libraryImportCandidateRepository
                        .MarkMatchingFailedAsync(candidate.Id, reason, userId, ct)
                        .ConfigureAwait(false);
                    if (marked)
                    {
                        await WriteAuditAsync(
                            operation: "FileErrored",
                            entityType: nameof(LibraryImportCandidate),
                            entityId: candidate.Id.ToString(),
                            detail: $"Match resolution failed for {Path.GetFileName(candidate.FilePath)}: {ex.Message}",
                            userId,
                            correlationId,
                            ct).ConfigureAwait(false);
                    }

                    return marked;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception writeEx)
        {
            logger.LogWarning(
                writeEx,
                "The match resolution failed to save the failure status for library import candidate " +
                "{LibraryImportCandidateId}",
                candidate.Id);
        }

        return persisted;
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
