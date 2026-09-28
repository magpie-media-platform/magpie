using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Observability;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Releases;

internal sealed class ReleaseEvaluationService(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IMaterializedMetadataRepository materializedMetadataRepository,
    IReleaseRepository releaseRepository,
    IDownloadRepository downloadRepository,
    IQualityTierListRepository qualityTierListRepository,
    IContentTypeRegistry contentTypeRegistry,
    IEnumerable<IContentFactProvider> contentFactProviders,
    TimeProvider timeProvider,
    IFileSystem fileSystem,
    ILogger<ReleaseEvaluationService> logger) : IReleaseEvaluationService
{
    private readonly Dictionary<Guid, Guid?> _qualityTierListIdsByContentHierarchyItem = [];

    private readonly Dictionary<Guid, QualityTierList?> _qualityTierListsById = [];

    private const int QueryPageSize = 200;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public bool IsAcceptableUpgradeOverCurrentFile(
        ReleaseAssessment assessment,
        Release release,
        ReleaseEvaluationContext context)
    {
        if (context.CurrentFileQualityRank is not { } floor)
        {
            return true;
        }

        var isHigherTier = assessment.MatchedRankOrdinal is { } ordinal && ordinal < floor;
        var parsedTitle = new ParsedReleaseTitle(release.Title);
        var isSameTierProperUpgrade = assessment.MatchedRankOrdinal == floor &&
            context.CurrentFileRevision is { } importedRevision &&
            parsedTitle.Revision.Version > importedRevision.Version &&
            IsSameGroup(parsedTitle.Group, context.CurrentFileGroup);

        return isHigherTier || isSameTierProperUpgrade;
    }

    internal static async Task<Release> ResolveDownloadReleaseAsync(
        IReleaseRepository releaseRepository,
        Download download,
        CancellationToken cancellationToken) =>
        (download.ReleaseId is { } releaseId
            ? await releaseRepository.GetByIdAsync(releaseId, cancellationToken).ConfigureAwait(false)
            : null) ?? new Release { Title = download.Title, Protocol = string.Empty };

    private static bool IsSameGroup(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public int EffectiveQualityRank(ReleaseAssessment assessment, ReleaseEvaluationContext context)
    {
        var actual = assessment.MatchedRankOrdinal ?? int.MaxValue;
        var cutoffs = context.QualityTierList?.QualityTierRanks
            .Where(rank => rank.IsCutoff)
            .Select(rank => rank.Ordinal)
            .ToArray() ?? [];
        if (cutoffs.Length == 0)
        {
            return actual;
        }

        var ceiling = context.CurrentFileQualityRank is { } current
            ? cutoffs.Where(cutoff => cutoff < current).DefaultIfEmpty(cutoffs.Min()).Max()
            : cutoffs.Max();
        return Math.Max(actual, ceiling);
    }

    private static QualityTierRule? FirstMatchingRule(
        IEnumerable<QualityTierRule> rules,
        RuleKind kind,
        IReadOnlyDictionary<string, string> properties) =>
        rules.FirstOrDefault(rule => rule.Kind == kind && Matches(rule.Criteria, properties));

    private static QualityTierRule? FirstUnmetAcceptRule(
        IEnumerable<QualityTierRule> rules,
        IReadOnlyDictionary<string, string> properties) =>
        rules.FirstOrDefault(rule => rule.Kind == RuleKind.Accept && !Matches(rule.Criteria, properties));

    private static string DescribeRule(QualityTierRule rule) =>
        string.IsNullOrWhiteSpace(rule.Name) ? $"{rule.Kind}" : $"{rule.Name}";

    private static bool Matches(
        CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>> criteria,
        IReadOnlyDictionary<string, string> properties)
    {
        if (criteria.And is null)
        {
            return true;
        }

        foreach (var dictionaryCriteria in criteria.And)
        {
            if (!MatchesDictionary(dictionaryCriteria, properties))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesDictionary(
        DictionaryCriteria<PropertyCriterion<string>> criteria,
        IReadOnlyDictionary<string, string> properties)
    {
        if (criteria.Items is null || criteria.Items.Count == 0)
        {
            return true;
        }

        foreach (var (key, criterion) in criteria.Items)
        {
            properties.TryGetValue(key, out var value);
            if (!MatchesProperty(value, criterion))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesProperty(string? actual, PropertyCriterion<string> criterion) =>
        Evaluate(criterion.Operator, actual, criterion.Value, criterion.Values, criterion.Negate);

    private static bool Evaluate(
        CriteriaValueOperator op,
        string? actual,
        string? value,
        IEnumerable<string>? values,
        bool negate)
    {
        var result = Apply(op, actual, value, values);
        return negate ? !result : result;
    }

    private static bool Apply(CriteriaValueOperator op, string? actual, string? value, IEnumerable<string>? values)
    {
        return op switch
        {
            CriteriaValueOperator.Equals => string.Equals(actual, value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.NotEquals => !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.IsNull => actual is null,
            CriteriaValueOperator.IsNotNull => actual is not null,
            CriteriaValueOperator.GreaterThan => CompareNumeric(actual, value) is { } greater && greater > 0,
            CriteriaValueOperator.GreaterThanOrEqual => CompareNumeric(actual, value) is { } greaterOrEqual &&
                greaterOrEqual >= 0,
            CriteriaValueOperator.LessThan => CompareNumeric(actual, value) is { } less && less < 0,
            CriteriaValueOperator.LessThanOrEqual => CompareNumeric(actual, value) is { } lessOrEqual &&
                lessOrEqual <= 0,
            CriteriaValueOperator.In => actual is not null &&
                values is not null &&
                values.Contains(actual, StringComparer.OrdinalIgnoreCase),
            CriteriaValueOperator.NotIn => actual is null ||
                values is null ||
                !values.Contains(actual, StringComparer.OrdinalIgnoreCase),
            CriteriaValueOperator.ContainsString => actual is not null &&
                value is not null &&
                actual.Contains(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.NotContainsString => actual is null ||
                value is null ||
                !actual.Contains(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.StartsWithString => actual is not null &&
                value is not null &&
                actual.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.NotStartsWithString => actual is null ||
                value is null ||
                !actual.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.EndsWithString => actual is not null &&
                value is not null &&
                actual.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.NotEndsWithString => actual is null ||
                value is null ||
                !actual.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.ContainsWord => actual is not null && value is not null && MatchesWord(actual, value),
            CriteriaValueOperator.Regex => actual is not null && value is not null && MatchesRegex(actual, value),
            var other => throw new NotSupportedException($"Operator {other} is not supported for field criteria"),
        };
    }

    private static bool MatchesWord(string actual, string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        return MatchesRegex(actual, $@"\b{Regex.Escape(value)}\b");
    }

    private static bool MatchesRegex(string actual, string pattern)
    {
        try
        {
            return Regex.IsMatch(actual, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int? CompareNumeric(string? actual, string? expected)
    {
        if (actual is null || expected is null)
        {
            return null;
        }

        if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualValue) ||
            !decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedValue))
        {
            return null;
        }

        return actualValue.CompareTo(expectedValue);
    }

    private static ReleaseAssessment QualityResult(
        Guid? matchedQualityTierId,
        int? matchedRankOrdinal,
        bool isAcceptable,
        bool isExcluded,
        bool isCutoffMet,
        IReadOnlyList<string> reasons) => new()
        {
            MatchResult = ReleaseMatchResult.Indeterminate,
            MatchedQualityTierId = matchedQualityTierId,
            MatchedRankOrdinal = matchedRankOrdinal,
            IsAcceptable = isAcceptable,
            IsExcluded = isExcluded,
            IsCutoffMet = isCutoffMet,
            QualityReasons = reasons,
        };

    public async Task<ReleaseEvaluationContext> CreateEvaluationContextAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveContentTargetAsync(contentHierarchyItemId, cancellationToken).ConfigureAwait(false);
        var contentFacts = target is null
            ? []
            : await CollectContentFactsAsync(target, cancellationToken).ConfigureAwait(false);

        Guid? qualityTierListId = null;
        try
        {
            qualityTierListId = await ResolveQualityTierListIdForContentAsync(contentHierarchyItemId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Quality tier list unavailable for content hierarchy item {ContentHierarchyItemId}, evaluating " +
                "releases without a quality score",
                contentHierarchyItemId);
        }

        var qualityTierList = qualityTierListId is { } tierListId
            ? await GetQualityTierListAsync(tierListId, cancellationToken).ConfigureAwait(false)
            : null;

        int? currentFileQualityRank = null;
        string? currentFileGroup = null;
        ReleaseRevision? currentFileRevision = null;
        var currentItem = await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var level = currentItem is null
            ? null
            : contentTypeRegistry.GetDefinitionFor(currentItem.ContentType)?.LevelDefinitions
                .FirstOrDefault(candidate => candidate.Slug == currentItem.Level);
        var primaryFiles = new List<MediaFile>();
        foreach (var file in (currentItem?.ContentItem?.MediaFiles ?? [])
            .OrderBy(file => file.FilePath, StringComparer.Ordinal))
        {
            if (level is not null && (level.AcceptedFileTypes.Count > 0
                ? await fileSystem.MatchesAnyFileTypeAsync(
                    file.FilePath, level.AcceptedFileTypes, cancellationToken).ConfigureAwait(false)
                : !await fileSystem.MatchesAnyFileTypeAsync(
                    file.FilePath, level.AcceptedSidecarTypes, cancellationToken).ConfigureAwait(false)))
            {
                primaryFiles.Add(file);
            }
        }

        if (currentItem?.ContentItem is { MediaReconciledAt: not null } mediaState)
        {
            currentFileQualityRank = mediaState.CurrentFileQualityRank;
            foreach (var file in currentFileQualityRank is null || qualityTierList is null ? [] : primaryFiles)
            {
                var (score, title) = await ScoreFileAsync(file, qualityTierList!, cancellationToken)
                    .ConfigureAwait(false);
                if (score.MatchedRankOrdinal != currentFileQualityRank)
                {
                    continue;
                }

                var parsedTitle = new ParsedReleaseTitle(title);
                if (currentFileRevision is not { } priorRevision ||
                    parsedTitle.Revision.Version > priorRevision.Version)
                {
                    currentFileGroup = parsedTitle.Group;
                    currentFileRevision = parsedTitle.Revision;
                }
            }
        }

        var linkedDownloadIds = primaryFiles
            .Where(file => file.DownloadId is not null)
            .Select(file => file.DownloadId!.Value)
            .Distinct()
            .ToList();
        if (currentFileQualityRank is null && qualityTierList is not null && linkedDownloadIds.Count > 0)
        {
            foreach (var downloadId in linkedDownloadIds)
            {
                var download = await downloadRepository.GetByIdAsync(downloadId, cancellationToken)
                    .ConfigureAwait(false);
                var release = download is null
                    ? null
                    : await ResolveDownloadReleaseAsync(releaseRepository, download, cancellationToken)
                        .ConfigureAwait(false);
                int? rank = null;
                try
                {
                    rank = release is null ? null : Score(release, qualityTierList, null).MatchedRankOrdinal;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Imported release {ReleaseId} scoring failed for current-quality basis",
                        release!.Id);
                }

                if (release is null || rank is null || rank >= currentFileQualityRank)
                {
                    continue;
                }

                var parsedTitle = new ParsedReleaseTitle(release.Title);
                currentFileQualityRank = rank;
                currentFileGroup = parsedTitle.Group;
                currentFileRevision = parsedTitle.Revision;
            }
        }
        else if (currentItem?.ContentItem is not { MediaReconciledAt: not null } && qualityTierList is not null)
        {
            var contentDownloads = await downloadRepository.QueryAllAsync(
                new DownloadQuery { ContentItemId = contentHierarchyItemId },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var importedDownload = (
                from download in contentDownloads
                where download.Status == DownloadStatus.Imported
                orderby download.CreatedAt descending
                select download).FirstOrDefault();

            var importedRelease = importedDownload is null
                ? null
                : await ResolveDownloadReleaseAsync(releaseRepository, importedDownload, cancellationToken)
                    .ConfigureAwait(false);
            if (importedRelease is not null)
            {
                try
                {
                    currentFileQualityRank = Score(importedRelease, qualityTierList, null).MatchedRankOrdinal;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(
                        ex,
                        "Imported release {ReleaseId} scoring failed for current-quality basis, cutoff uses " +
                        "per-release scoring",
                        importedRelease.Id);
                }

                var parsedTitle = new ParsedReleaseTitle(importedRelease.Title);
                currentFileGroup = parsedTitle.Group;
                currentFileRevision = parsedTitle.Revision;
            }
        }

        return new ReleaseEvaluationContext
        {
            ContentHierarchyItemId = contentHierarchyItemId,
            Target = target,
            ContentFacts = contentFacts,
            QualityTierListId = qualityTierListId,
            QualityTierList = qualityTierList,
            CurrentFileQualityRank = currentFileQualityRank,
            CurrentFileGroup = currentFileGroup,
            CurrentFileRevision = currentFileRevision,
        };
    }

    public ReleaseAssessment AssessMatch(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        ReleaseEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(releaseFacts);
        ArgumentNullException.ThrowIfNull(context);

        var matchResult = ReleaseMatchResult.NoMatch;
        if (context.Target is { } target)
        {
            matchResult = GenericMatch(target, context.ContentFacts, release, releaseFacts);
            if (contentTypeRegistry.FindProviderFor(target.Item.ContentType)
                is IReleaseMatchContentTypeDefinitionProvider provider)
            {
                try
                {
                    var providerMatch = provider.IsMatch(target, context.ContentFacts, release, releaseFacts);
                    if (matchResult is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident ||
                        providerMatch is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident)
                    {
                        matchResult = providerMatch > matchResult ? providerMatch : matchResult;
                    }
                    else if (providerMatch == ReleaseMatchResult.NoMatch)
                    {
                        matchResult = ReleaseMatchResult.NoMatch;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Release match provider for content type {ContentType} failed for release {ReleaseTitle}",
                        target.Item.ContentType,
                        release.Title);
                }
            }
        }

        ReleaseAssessment? score = null;
        try
        {
            score = context.QualityTierList is { } list
                ? Score(release, list, context.CurrentFileQualityRank)
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to score release {ReleaseTitle}, it is evaluated without a quality score",
                release.Title);
        }

        ReleaseTelemetry.ReleasesEvaluated.Add(
            1,
            new KeyValuePair<string, object?>(
                "match_result",
                matchResult switch
                {
                    ReleaseMatchResult.Identity => "identity",
                    ReleaseMatchResult.Confident => "confident",
                    ReleaseMatchResult.Indeterminate => "indeterminate",
                    _ => "no_match",
                }));

        return new ReleaseAssessment
        {
            MatchResult = matchResult,
            MatchedQualityTierId = score?.MatchedQualityTierId,
            MatchedRankOrdinal = score?.MatchedRankOrdinal,
            IsAcceptable = score?.IsAcceptable,
            IsExcluded = score?.IsExcluded,
            IsCutoffMet = score?.IsCutoffMet,
            QualityReasons = score?.QualityReasons ?? [],
        };
    }

    public async Task<ReleaseAssessment> AssessForContentAsync(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var context = await CreateEvaluationContextAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var assessment = AssessMatch(release, releaseFacts, context);
        return assessment.IsAcceptable == true && !IsAcceptableUpgradeOverCurrentFile(assessment, release, context)
            ? assessment with { IsAcceptable = false }
            : assessment;
    }

    public async Task<ReleaseScopeResolution> ResolveScopeAsync(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var searchedContext = await CreateEvaluationContextAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var searchedMatch = AssessMatch(release, releaseFacts, searchedContext);
        return await ResolveScopeAsync(
            release,
            releaseFacts,
            searchedContext,
            searchedMatch,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ReleaseScopeResolution> ResolveScopeAsync(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        ReleaseEvaluationContext searchedContext,
        ReleaseAssessment searchedMatch,
        CancellationToken cancellationToken = default)
    {
        var contentHierarchyItemId = searchedContext.ContentHierarchyItemId;
        List<object>? scopeTrace = logger.IsEnabled(LogLevel.Trace) ? [] : null;
        if (scopeTrace is not null)
        {
            scopeTrace.Add(new
            {
                Decision = "searched-anchor",
                ContentHierarchyItemId = contentHierarchyItemId,
                MatchResult = searchedMatch.MatchResult.ToString(),
                Accepted = searchedMatch.MatchResult is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident,
            });
        }

        if (searchedMatch.MatchResult is not (ReleaseMatchResult.Identity or ReleaseMatchResult.Confident) ||
            searchedContext.Target is null)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace(
                    "SCOPE ReleaseSearch {ScopeJson}",
                    JsonSerializer.Serialize(new
                    {
                        release.Id,
                        release.Title,
                        SearchedContentHierarchyItemId = contentHierarchyItemId,
                        ReleaseFacts = releaseFacts,
                        Decisions = scopeTrace,
                        ScopeNodeIds = Array.Empty<Guid>(),
                        ActionableNodeIds = Array.Empty<Guid>(),
                    }));
            }

            return new ReleaseScopeResolution([], []);
        }

        var scoped = new HashSet<Guid>();
        await ResolveScopedNodesAsync(release, releaseFacts, searchedContext, scoped, scopeTrace, cancellationToken)
            .ConfigureAwait(false);
        var actionable = await ResolveLowestScopedNodeIdsAsync(scoped, cancellationToken).ConfigureAwait(false);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace(
                "SCOPE ReleaseSearch {ScopeJson}",
                JsonSerializer.Serialize(new
                {
                    release.Id,
                    release.Title,
                    SearchedContentHierarchyItemId = contentHierarchyItemId,
                    ReleaseFacts = releaseFacts,
                    Decisions = scopeTrace,
                    ScopeNodeIds = scoped,
                    ActionableNodeIds = actionable,
                }));
        }

        return new ReleaseScopeResolution([.. scoped], actionable);
    }

    private async Task<bool> ResolveScopedNodesAsync(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        ReleaseEvaluationContext context,
        HashSet<Guid> scoped,
        List<object>? scopeTrace,
        CancellationToken cancellationToken)
    {
        if (context.Target is not { } target)
        {
            if (scopeTrace is not null)
            {
                scopeTrace.Add(new { Decision = "target-unresolved", context.ContentHierarchyItemId, });
            }

            return false;
        }

        var hasKnownChild = false;
        var coversAllKnownLeaves = true;
        foreach (var childLevel in contentTypeRegistry.GetChildLevels(target.Item.ContentType, target.Item.Level))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var children = await ListChildHierarchyItemsAsync(target.Item.Id, childLevel.Slug, cancellationToken)
                .ConfigureAwait(false);
            foreach (var child in children)
            {
                hasKnownChild = true;
                var childContext = await CreateEvaluationContextAsync(child.Id, cancellationToken)
                    .ConfigureAwait(false);
                var childMatch = AssessMatch(release, releaseFacts, childContext);
                if (scopeTrace is not null)
                {
                    scopeTrace.Add(new
                    {
                        Decision = "child-match",
                        ParentContentHierarchyItemId = target.Item.Id,
                        ChildContentHierarchyItemId = child.Id,
                        ChildLevel = child.Level,
                        MatchResult = childMatch.MatchResult.ToString(),
                        Accepted = childMatch.MatchResult is
                            ReleaseMatchResult.Identity or
                            ReleaseMatchResult.Confident,
                    });
                }

                if (childMatch.MatchResult is not (ReleaseMatchResult.Identity or ReleaseMatchResult.Confident))
                {
                    coversAllKnownLeaves = false;
                    continue;
                }

                var childCoverage = await ResolveScopedNodesAsync(
                    release,
                    releaseFacts,
                    childContext,
                    scoped,
                    scopeTrace,
                    cancellationToken)
                    .ConfigureAwait(false);
                coversAllKnownLeaves &= childCoverage;
            }
        }

        var included = !hasKnownChild || coversAllKnownLeaves;
        if (included)
        {
            scoped.Add(target.Item.Id);
        }

        if (scopeTrace is not null)
        {
            scopeTrace.Add(new
            {
                Decision = "coverage",
                ContentHierarchyItemId = target.Item.Id,
                target.Item.Level,
                HasKnownChild = hasKnownChild,
                CoversAllKnownLeaves = coversAllKnownLeaves,
                Included = included,
            });
        }

        return coversAllKnownLeaves;
    }

    private async Task<IReadOnlyList<Guid>> ResolveLowestScopedNodeIdsAsync(
        IReadOnlyCollection<Guid> scoped,
        CancellationToken cancellationToken)
    {
        if (scoped.Count == 0)
        {
            return [];
        }

        var nodes = new List<ContentHierarchyItem>(scoped.Count);
        foreach (var contentHierarchyItemId in scoped)
        {
            var node = await contentHierarchyItemRepository
                .GetByIdAsync(contentHierarchyItemId, cancellationToken)
                .ConfigureAwait(false);
            if (node is not null)
            {
                nodes.Add(node);
            }
        }

        return
        [.. nodes.Where(node => nodes.All(candidate => candidate.ParentId != node.Id)).Select(node => node.Id)];
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> ListChildHierarchyItemsAsync(
        Guid parentId,
        string level,
        CancellationToken cancellationToken)
    {
        var items = await contentHierarchyItemRepository.QueryAllAsync(
            new ContentHierarchyItemCriteria
            {
                ParentId = new CriteriaSet<PropertyCriterion<Guid>>
                {
                    And =
                    [
                        new PropertyCriterion<Guid>
                        {
                            Operator = CriteriaValueOperator.Equals,
                            Value = parentId,
                        },
                    ],
                },
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And =
                    [
                        new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.Equals,
                            Value = level,
                        },
                    ],
                },
            },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        return items;
    }

    public async Task<IReadOnlyList<ReleaseFileAssignment>> AssignFilesToLeavesAsync(
        Release release,
        ContentHierarchyItem parent,
        IReadOnlyList<ContentHierarchyItem> leaves,
        IReadOnlyList<ReleaseFileRef> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(files);

        var contexts = new List<ReleaseEvaluationContext>(leaves.Count);
        foreach (var leaf in leaves)
        {
            contexts.Add(await CreateEvaluationContextAsync(leaf.Id, cancellationToken).ConfigureAwait(false));
        }

        var assignments = new List<ReleaseFileAssignment>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leafId = ResolveLeaf(release with { Title = file.FileName }, contexts, cancellationToken);
            assignments.Add(new() { FileKey = file.Key, LeafContentHierarchyItemId = leafId });
        }

        return assignments;
    }

    public async Task<ReleaseAssessment?> ScoreForContentAsync(
        Release release,
        Guid contentHierarchyItemId,
        int? currentQualityOrdinal = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var qualityTierListId = await ResolveQualityTierListIdForContentAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (qualityTierListId is null)
        {
            return null;
        }

        var list = await GetQualityTierListAsync(qualityTierListId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (list is null)
        {
            logger.LogDebug(
                "Quality tier list {QualityTierListId} was not found, returning null score for release {ReleaseId}",
                qualityTierListId,
                release.Id);
            return null;
        }

        return Score(release, list, currentQualityOrdinal);
    }

    /// <inheritdoc />
    public async Task<ReleaseAssessment?> ScoreFileForContentAsync(
        MediaFile mediaFile,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var listId = await ResolveQualityTierListIdForContentAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var list = listId is { } id
            ? await GetQualityTierListAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
        return list is null
            ? null
            : (await ScoreFileAsync(mediaFile, list, cancellationToken).ConfigureAwait(false)).Score;
    }

    private async Task<(ReleaseAssessment Score, string Title)> ScoreFileAsync(
        MediaFile mediaFile,
        QualityTierList list,
        CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = Path.GetFileName(mediaFile.FilePath),
            ["Size"] = mediaFile.FileSize.ToString(CultureInfo.InvariantCulture),
        };
        if (mediaFile.MediaInfo is { } info)
        {
            if (info.Container is { } container)
            {
                properties["Container"] = container;
            }

            if (info.BitRate is { } bitRate)
            {
                properties["BitRate"] = bitRate.ToString(CultureInfo.InvariantCulture);
            }

            var languages = info.Tracks.Select(track => track.Language)
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            properties["Language"] = string.Join(",", languages);
            foreach (var language in languages)
            {
                properties[$"Language.{language}"] = "true";
            }

            var video = info.Tracks
                .FirstOrDefault(track => track.Type.Equals("Video", StringComparison.OrdinalIgnoreCase));
            if (video?.Height is { } height)
            {
                properties["Height"] = height.ToString(CultureInfo.InvariantCulture);
            }

            if (video?.Width is { } width)
            {
                properties["Width"] = width.ToString(CultureInfo.InvariantCulture);
            }

            if (video?.Codec is { } codec)
            {
                properties["VideoCodec"] = codec;
            }
        }

        var score = Score(properties, list, currentQualityOrdinal: null);
        var download = score.MatchedRankOrdinal is null && score.IsExcluded != true &&
            mediaFile.DownloadId is { } downloadId
            ? await downloadRepository.GetByIdAsync(downloadId, cancellationToken).ConfigureAwait(false)
            : null;
        var release = download is null
            ? null
            : await ResolveDownloadReleaseAsync(releaseRepository, download, cancellationToken).ConfigureAwait(false);
        var imported = release is null ? null : Score(release, list, currentQualityOrdinal: null);
        return imported?.MatchedRankOrdinal is null
            ? (score, Path.GetFileNameWithoutExtension(mediaFile.FilePath))
            : (imported, release!.Title);
    }

    private Guid? ResolveLeaf(
        Release release,
        IReadOnlyList<ReleaseEvaluationContext> contexts,
        CancellationToken cancellationToken)
    {
        Guid? matched = null;
        foreach (var context in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = AssessMatch(release, [], context);
            if (result.MatchResult is not (ReleaseMatchResult.Identity or ReleaseMatchResult.Confident))
            {
                continue;
            }

            if (matched is not null)
            {
                return null;
            }

            matched = context.ContentHierarchyItemId;
        }

        return matched;
    }

    private ReleaseMatchResult GenericMatch(
        ContentTarget target,
        IReadOnlyList<IReadOnlyList<ContentFact>> contentFacts,
        Release release,
        IReadOnlyList<ContentFact> releaseFacts)
    {
        if (releaseFacts.Count == 1 &&
            string.IsNullOrWhiteSpace(releaseFacts[0].Value) &&
            releaseFacts[0].Facets.Count == 1)
        {
            var (releaseKey, releaseValue) = releaseFacts[0].Facets.Single();
            if (!string.IsNullOrWhiteSpace(releaseValue))
            {
                var externalIds = target.Metadata.TryGetValue(target.Item.Id, out var metadata)
                    ? metadata.ExternalIds ?? []
                    : [];
                foreach (var externalId in externalIds)
                {
                    if (string.IsNullOrWhiteSpace(externalId.Source) || string.IsNullOrWhiteSpace(externalId.Value))
                    {
                        continue;
                    }

                    if (string.Equals(
                            releaseKey.NormalizeExternalIdentifierName(),
                            externalId.Source.NormalizeExternalIdentifierName(),
                            StringComparison.Ordinal) &&
                        string.Equals(
                            releaseValue.NormalizeExternalIdentifierValue(),
                            externalId.Value.NormalizeExternalIdentifierValue(),
                            StringComparison.Ordinal))
                    {
                        return ReleaseMatchResult.Identity;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(release.Title))
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var releaseTitle = release.Title.Trim();
        foreach (var factSet in contentFacts)
        {
            if (factSet.Count == 0 ||
                factSet.Any(fact => string.IsNullOrWhiteSpace(fact.Value) || fact.Facets.Count > 0))
            {
                continue;
            }

            var title = string.Join(' ', factSet.Select(fact => fact.Value!.Trim()));
            if (string.Equals(releaseTitle, title, StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseMatchResult.Confident;
            }
        }

        var lineageTitle = string.Join(
            ' ',
            target.Lineage
                .Reverse()
                .Select(item => AvailableTitle(target, item))
                .Where(title => !string.IsNullOrWhiteSpace(title)));
        return string.Equals(releaseTitle, lineageTitle, StringComparison.OrdinalIgnoreCase)
            ? ReleaseMatchResult.Confident
            : ReleaseMatchResult.Indeterminate;
    }

    private async Task<ContentTarget?> ResolveContentTargetAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            return null;
        }

        var containerIds = lineage.Select(item => item.MetadataContainerId).Distinct().ToArray();
        var materializedByContainerId = new Dictionary<Guid, MaterializedMetadata>();
        foreach (var batch in containerIds.Chunk(500))
        {
            var materialized = await materializedMetadataRepository.QueryAsync(
                new() { ContainerIds = batch },
                new() { PageNumber = 1, PageSize = batch.Length },
                null,
                cancellationToken).ConfigureAwait(false);
            foreach (var metadataItem in materialized.Items)
            {
                materializedByContainerId.Add(metadataItem.ContainerId, metadataItem);
            }
        }

        var metadata = lineage
            .Where(item => materializedByContainerId.ContainsKey(item.MetadataContainerId))
            .ToDictionary(item => item.Id, item => materializedByContainerId[item.MetadataContainerId]);

        return new ContentTarget
        {
            Item = lineage[0],
            Lineage = lineage,
            Metadata = metadata,
        };
    }

    private async Task<IReadOnlyList<IReadOnlyList<ContentFact>>> CollectContentFactsAsync(
        ContentTarget target,
        CancellationToken cancellationToken)
    {
        var factSets = new List<IReadOnlyList<ContentFact>>();
        if (target.Lineage.Count == 1)
        {
            var title = AvailableTitle(target, target.Item);
            if (!string.IsNullOrWhiteSpace(title))
            {
                factSets.Add([new ContentFact { Value = title }]);
            }
        }
        else
        {
            var titles = new List<string>();
            for (var index = target.Lineage.Count - 1; index >= 0; index--)
            {
                var title = AvailableTitle(target, target.Lineage[index]);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    titles.Add(title);
                }
            }

            var joined = string.Join(' ', titles);
            if (!string.IsNullOrWhiteSpace(joined))
            {
                factSets.Add([new ContentFact { Value = joined }]);
            }
        }

        var externalIds = target.Metadata.TryGetValue(target.Item.Id, out var metadata)
            ? metadata.ExternalIds ?? []
            : [];
        foreach (var externalId in externalIds)
        {
            if (string.IsNullOrWhiteSpace(externalId.Source) || string.IsNullOrWhiteSpace(externalId.Value))
            {
                continue;
            }

            factSets.Add(
            [
                new ContentFact
                {
                    Facets = new Dictionary<string, string>
                    {
                        [externalId.Source.Trim()] = externalId.Value.Trim(),
                    },
                },
            ]);
        }

        foreach (var contentFactProvider in contentFactProviders)
        {
            if (!contentFactProvider.CanProvideFacts(target))
            {
                continue;
            }

            var provided = await contentFactProvider
                .GetFactsAsync(target, cancellationToken)
                .ConfigureAwait(false);
            factSets.AddRange(provided);
        }

        return factSets;
    }

    private string? AvailableTitle(ContentTarget target, ContentHierarchyItem item)
    {
        var title = target.Metadata.TryGetValue(item.Id, out var metadata)
            ? metadata.Title
            : null;
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim();
    }

    private async Task<Guid?> ResolveQualityTierListIdForContentAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        if (_qualityTierListIdsByContentHierarchyItem.TryGetValue(contentHierarchyItemId, out var resolved))
        {
            return resolved;
        }

        var item = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var tierListId = item?.EffectiveQualityTierListId;
        _qualityTierListIdsByContentHierarchyItem[contentHierarchyItemId] = tierListId;
        return tierListId;
    }

    private async Task<QualityTierList?> GetQualityTierListAsync(
        Guid qualityTierListId,
        CancellationToken cancellationToken)
    {
        if (_qualityTierListsById.TryGetValue(qualityTierListId, out var cached))
        {
            return cached;
        }

        var list = await qualityTierListRepository
            .GetByIdAsync(qualityTierListId, cancellationToken)
            .ConfigureAwait(false);
        _qualityTierListsById[qualityTierListId] = list;
        return list;
    }

    private ReleaseAssessment Score(Release release, QualityTierList list, int? currentQualityOrdinal)
    {
        return Score(BuildPropertyBag(release), list, currentQualityOrdinal);
    }

    private ReleaseAssessment Score(
        IReadOnlyDictionary<string, string> properties,
        QualityTierList list,
        int? currentQualityOrdinal)
    {
        var listRules = list.Rules ?? [];
        var listExclude = FirstMatchingRule(listRules, RuleKind.Exclude, properties);
        if (listExclude is not null)
        {
            ReleaseTelemetry.QualityDecisions.Add(
                1,
                new KeyValuePair<string, object?>("decision", "excluded"),
                new KeyValuePair<string, object?>("cutoff_met", false));
            return QualityResult(
                matchedQualityTierId: null,
                matchedRankOrdinal: null,
                isAcceptable: false,
                isExcluded: true,
                isCutoffMet: false,
                [$"Excluded by rule {DescribeRule(listExclude)}."]);
        }

        var listAcceptFailure = FirstUnmetAcceptRule(listRules, properties);
        if (listAcceptFailure is not null)
        {
            ReleaseTelemetry.QualityDecisions.Add(
                1,
                new KeyValuePair<string, object?>("decision", "accept_rule_unmet"),
                new KeyValuePair<string, object?>("cutoff_met", false));
            return QualityResult(
                matchedQualityTierId: null,
                matchedRankOrdinal: null,
                isAcceptable: false,
                isExcluded: false,
                isCutoffMet: false,
                [$"Did not match required rule {DescribeRule(listAcceptFailure)}."]);
        }

        var ranks = (
            from rank in list.QualityTierRanks
            where rank.QualityTier is not null
            orderby rank.Ordinal, rank.QualityTier!.Id
            select rank).ToList();

        var matched = new List<QualityTierListRank>();
        foreach (var rank in ranks)
        {
            var tier = rank.QualityTier!;
            var tierRules = tier.Rules ?? [];
            if (FirstMatchingRule(tierRules, RuleKind.Exclude, properties) is not null ||
                FirstUnmetAcceptRule(tierRules, properties) is not null)
            {
                continue;
            }

            if (Matches(tier.Criteria, properties))
            {
                matched.Add(rank);
            }
        }

        var matchedRank = matched.FirstOrDefault();
        var isAcceptable = matchedRank is not null;
        var cutoffOrdinals = ranks.Where(rank => rank.IsCutoff).Select(rank => rank.Ordinal).ToList();
        bool isCutoffMet;
        if (cutoffOrdinals.Count == 0)
        {
            isCutoffMet = isAcceptable;
        }
        else if (matchedRank is null)
        {
            isCutoffMet = false;
        }
        else
        {
            var basis = currentQualityOrdinal ?? matchedRank.Ordinal;
            var reachedCutoffs = cutoffOrdinals.Where(ordinal => ordinal <= basis).ToList();
            var effectiveCutoff = reachedCutoffs.Count > 0 ? reachedCutoffs.Max() : cutoffOrdinals.Max();
            isCutoffMet = matchedRank.Ordinal <= effectiveCutoff;
        }

        var reasons = isAcceptable
            ? matched.Select(rank => $"Matched {rank.QualityTier!.Name}").ToArray()
            : ["No quality tier matched."];

        ReleaseTelemetry.QualityDecisions.Add(
            1,
            new KeyValuePair<string, object?>("decision", matchedRank is null ? "no_tier_matched" : "matched"),
            new KeyValuePair<string, object?>("cutoff_met", isCutoffMet));

        return QualityResult(
            matchedRank?.QualityTierId,
            matchedRank?.Ordinal,
            isAcceptable,
            isExcluded: false,
            isCutoffMet,
            reasons);
    }

    private Dictionary<string, string> BuildPropertyBag(Release release)
    {
        const string titleField = "Title";
        const string protocolField = "Protocol";
        const string sizeField = "Size";
        const string ageField = "Age";
        const string languageField = "Language";
        const string categoryField = "Category";

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        properties[titleField] = release.Title ?? string.Empty;
        properties[protocolField] = release.Protocol;
        properties[sizeField] = release.Size?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        if (release.PublishDate != default)
        {
            var ageDays = (long)(timeProvider.GetUtcNow().UtcDateTime.Date - release.PublishDate.Date).TotalDays;
            properties[ageField] = Math.Max(0, ageDays).ToString(CultureInfo.InvariantCulture);
        }

        properties[languageField] = string.Join(",", release.Languages);
        foreach (var language in release.Languages)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                properties[$"{languageField}.{language}"] = "true";
            }
        }

        properties[categoryField] = string.Join(",", release.Categories);
        foreach (var category in release.Categories)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                properties[$"{categoryField}.{category}"] = "true";
            }
        }

        return properties;
    }
}
