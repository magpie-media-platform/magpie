using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Content;

internal sealed class MetadataSearchService(
    IEnumerable<IContentHierarchyMetadataProvider> metadataProviders,
    IEnumerable<IContentHierarchyMetadataSearcher> searchers,
    IContentTypeRegistry contentTypeRegistry,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    TimeProvider timeProvider,
    ILogger<MetadataSearchService> logger,
    IMemoryCache memoryCache) : IMetadataSearchService
{
    private const int TierExactTitle = 0;
    private const int TierTitlePrefix = 1;
    private const int TierTitleContains = 2;
    private const int TierTitleUnrelated = 3;

    private const int YearNeutral = 0;
    private const int YearMissingPenalty = int.MaxValue;

    private const int MinPlausibleYear = 1000;
    private const int MaxPlausibleYear = 2100;

    private const int ProviderSearchPageSize = 25;

    private const string NoProviderAvailableWarning =
        "No metadata provider is available to run this search. Enable a provider for this content and search again.";

    private enum MatchOutcome
    {
        Matched,

        Violated,

        Unverifiable
    }

    public async Task<ProviderMetadataCandidateSearchResult> SearchCandidatesAsync(
        ContentFilterCriteria criteria,
        string? contentType,
        int page,
        CancellationToken cancellationToken = default)
    {
        var resolvedContentType = !string.IsNullOrWhiteSpace(contentType)
            ? contentType.Trim()
            : ResolveContentType(criteria);

        IReadOnlyList<string> contentTypes;
        if (!string.IsNullOrWhiteSpace(resolvedContentType))
        {
            contentTypes = [resolvedContentType];
        }
        else
        {
            contentTypes = [.. (
                from d in contentTypeRegistry.Definitions
                where !string.IsNullOrWhiteSpace(d.Slug)
                select d.Slug).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (contentTypes.Count == 0)
            {
                logger.LogInformation(
                    "The metadata service skipped the provider candidate search. No content types are registered");
                return ProviderMetadataCandidateSearchResult.Empty;
            }

            logger.LogInformation(
                "The metadata candidate search has no content type constraint. The service will search " +
                "all {Count} types",
                contentTypes.Count);
        }

        var warningsSeen = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var prepared = PrepareSearch(criteria);
        const string defaultLanguagePreference = "";
        var requestedPage = Math.Max(1, page);
        var maximumResults = requestedPage > int.MaxValue / ProviderSearchPageSize
            ? int.MaxValue
            : requestedPage * ProviderSearchPageSize;
        var seen = new HashSet<(string, string)>();
        var inLibraryCandidateKeys = new HashSet<(string, string)>();
        var groups = new List<IReadOnlyList<MetadataSearchCandidate>>();
        var mergedCount = 0;
        var validQueries = new List<(string ContentType, ContentHierarchyItemCriteria Query)>();
        var rejectedQueries = new List<(
            string ContentType,
            IReadOnlyList<ContentHierarchyLevelDefinition> Levels)>();
        foreach (var type in contentTypes)
        {
            var query = ProviderQuery(criteria, type);
            if (!query.Level.And.Any())
            {
                validQueries.Add((type, query));
                continue;
            }

            var definedLevels = contentTypeRegistry.GetDefinitionFor(type)?.LevelDefinitions;
            var levels = definedLevels is { Count: > 0 }
                ? definedLevels
                : contentTypeRegistry.GetRootLevels(type);
            var matchingLevels = MatchingDeclaredLevels(levels, query.Level);
            if (matchingLevels.Count > 0)
            {
                validQueries.AddRange(matchingLevels.Select(level =>
                    (type, query with
                    {
                        Level = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Value = level.Slug
                                }
                            ]
                        }
                    })));
                continue;
            }

            rejectedQueries.Add((type, levels));
        }

        if (validQueries.Count == 0)
        {
            foreach (var rejectedQuery in rejectedQueries)
            {
                var validLevels = string.Join(", ", rejectedQuery.Levels.Select(level => level.Slug));
                AddWarning(
                    $"No provider supports the requested level for {rejectedQuery.ContentType}. " +
                    $"Valid levels: {validLevels}.",
                    warningsSeen,
                    warnings);
                logger.LogInformation(
                    "Skipped {ContentType}: invalid level filter. Valid levels: {ValidLevels}",
                    rejectedQuery.ContentType,
                    validLevels);
            }
        }

        var invocations = (
            from validQuery in validQueries
            from provider in searchers.OfType<IContentHierarchyMetadataAdvancedSearcher>()
            where SupportsContentType(provider.SupportedContentTypes, validQuery.ContentType)
            select new ProviderSearchInvocation(validQuery.ContentType, provider, validQuery.Query))
            .ToList();
        var providersQueried = invocations.Count;
        var searches = invocations.Select(invocation => SearchProviderAsync(
            logger,
            invocation,
            maximumResults,
            defaultLanguagePreference,
            memoryCache,
            cancellationToken));
        var providerResults = await Task.WhenAll(searches).ConfigureAwait(false);

        var providerSearchTruncated = false;
        foreach (var execution in providerResults)
        {
            if (execution.Warning is not null)
            {
                AddWarning(execution.Warning, warningsSeen, warnings);
                continue;
            }

            if (execution.Result is not { Supported: true } result)
            {
                AddWarning(
                    ProviderSearchFailedWarning(execution.Invocation.Provider.ProviderType),
                    warningsSeen,
                    warnings);
                continue;
            }

            providerSearchTruncated |= result.PageInfo.HasMore;
            var provider = execution.Invocation.Provider;
            if (result.Warnings is not null)
            {
                foreach (var warning in result.Warnings)
                {
                    if (warning is null || string.IsNullOrWhiteSpace(warning))
                    {
                        continue;
                    }

                    logger.LogWarning(
                        "Metadata provider {Provider} reported search diagnostic {Diagnostic}",
                        provider.ProviderType,
                        warning);
                    AddWarning(
                        ProviderWarning(provider.ProviderType, warning),
                        warningsSeen,
                        warnings);
                }
            }

            var candidates = await PrepareCandidatesAsync(
                provider,
                result.Matches,
                prepared,
                defaultLanguagePreference,
                inLibraryCandidateKeys,
                cancellationToken).ConfigureAwait(false);
            var guarded = FilterCandidatesAgainstCriteria(candidates, prepared.Criteria);
            var group = new List<MetadataSearchCandidate>(guarded.Count);
            foreach (var candidate in guarded)
            {
                if (!HasIdentity(candidate))
                {
                    continue;
                }

                var key = (candidate.ExternalIdSource.ToLowerInvariant(), candidate.ExternalId.ToLowerInvariant());
                if (seen.Add(key))
                {
                    group.Add(candidate);
                }
            }

            groups.Add(group);
            mergedCount += group.Count;
        }

        if (providersQueried == 0)
        {
            if (validQueries.Count > 0)
            {
                logger.LogInformation(
                    "The metadata search found no providers for types [{ContentTypes}]",
                    string.Join(", ", validQueries.Select(query => query.ContentType)));
                AddWarning(NoProviderAvailableWarning, warningsSeen, warnings);
            }

            return ProviderMetadataCandidateSearchResult.Empty with { Warnings = warnings };
        }

        var ranked = RankByTitleQuery(prepared.TitleQuery, groups);
        var offset = ((long)requestedPage - 1) * ProviderSearchPageSize;
        var pageMatches = offset > int.MaxValue
            ? []
            : ranked.Skip((int)offset).Take(ProviderSearchPageSize).ToList();
        var matches = pageMatches.Select(candidate => new MetadataSearchMatch
        {
            Candidate = candidate,
            InLibrary = inLibraryCandidateKeys.Contains((
                candidate.ExternalIdSource.ToLowerInvariant(),
                candidate.ExternalId.ToLowerInvariant()))
        }).ToArray();
        var ownedCount = matches.Count(match => match.InLibrary);
        logger.LogInformation(
            "Merged {Merged} results from {Providers} providers; {Owned} returned results are in the library",
            mergedCount,
            providersQueried,
            ownedCount);

        return new ProviderMetadataCandidateSearchResult
        {
            Matches = matches,
            PageItemCount = pageMatches.Count,
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = requestedPage,
                HasMore = providerSearchTruncated || ranked.Count > offset + ProviderSearchPageSize
            },
            Warnings = warnings
        };
    }

    public async Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
        string query,
        string contentType,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(contentType))
        {
            return [];
        }

        var matchingSearchers = searchers
            .Where(searcher => SupportsContentType(searcher.SupportedContentTypes, contentType))
            .ToArray();
        if (matchingSearchers.Length == 0)
        {
            return [];
        }

        var tasks = contentTypeRegistry.GetRootLevels(contentType)
            .SelectMany(level => matchingSearchers.Select(searcher => SearchOneAsync(
                searcher,
                level,
                query,
                Math.Max(1, page),
                cancellationToken)))
            .ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return Rank(query, results, item => item.Title, item => item.Year ?? item.ReleaseDate?.Year);
    }

    private static async Task<IReadOnlyList<MetadataSearchCandidate>> SearchOneAsync(
        IContentHierarchyMetadataSearcher searcher,
        ContentHierarchyLevelDefinition level,
        string query,
        int page,
        CancellationToken cancellationToken)
    {
        var results = await searcher
            .SearchAsync(level, query, defaultLanguagePreference: string.Empty, page, cancellationToken)
            .ConfigureAwait(false);
        return [.. results.Select(item => string.IsNullOrWhiteSpace(item.Level)
            ? item with { Level = level.Slug }
            : item)];
    }

    public async Task<ContentPreview?> PreviewByExternalIdAsync(
        string providerType,
        string externalIdKey,
        string externalId,
        string contentType,
        string? levelSlug,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerType) ||
            string.IsNullOrWhiteSpace(externalIdKey) ||
            string.IsNullOrWhiteSpace(externalId) ||
            string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException(
                "Provider type, external ID source, external ID, and content type are all required.");
        }

        var resolvedLevel = contentTypeRegistry.GetRootLevelSlug(contentType, levelSlug);

        var contentTypeProviders = metadataProviders
            .Where(p => p.SupportedContentTypes.Any(supported => supported == "*" || supported == contentType))
            .ToArray();

        var providers = contentTypeProviders.Where(p => p.ProviderType == providerType).ToArray();
        if (providers.Length == 0)
        {
            providers = contentTypeProviders;
        }

        if (providers.Length == 0)
        {
            logger.LogInformation(
                "Content type {ContentType} has no metadata provider for {Key}={Value}. The metadata " +
                "service will return no preview",
                contentType,
                externalIdKey,
                externalId);
            return null;
        }

        var seedContribution = new MetadataContribution
        {
            Id = Guid.NewGuid(),
            ProviderType = providerType,
            Ordinal = 0,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        seedContribution.ExternalIds.Add(new ExternalId { Source = externalIdKey, Value = externalId, Ordinal = 0, });

        var transientItem = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ParentId = null,
            ContentType = contentType,
            Level = resolvedLevel,
            Slug = "pending",
            Name = "pending",
            PendingMetadataRefresh = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };

        var existing = new[] { seedContribution };

        MetadataContribution? primary = null;
        var contributions = new List<MetadataContribution>();
        var relatedByContribution =
            new Dictionary<MetadataContribution, IReadOnlyList<SubjectRelationshipDefinition>>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MetadataContribution? contribution;
            try
            {
                var result = await provider.TryGetMetadataAsync(
                    new()
                    {
                        Item = transientItem,
                        ExistingMetadata = existing,
                        DefaultLanguagePreference = string.Empty,
                    },
                    cancellationToken).ConfigureAwait(false);
                contribution = result?.Contribution;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Metadata provider {ProviderType} failed to fetch a preview for {Key}={Value}. " +
                    "The metadata service skipped this provider",
                    provider.ProviderType,
                    externalIdKey,
                    externalId);
                continue;
            }

            if (contribution is null)
            {
                continue;
            }

            primary ??= contribution;
            contributions.Add(contribution);

            if (provider is IContentHierarchyMetadataRelationshipProvider relationshipProvider)
            {
                try
                {
                    var relationships = await relationshipProvider.GetRelationshipsAsync(
                        new MetadataRelationshipRequest
                        {
                            Item = transientItem,
                            Contribution = contribution,
                            ExistingMetadata = existing,
                        },
                        cancellationToken).ConfigureAwait(false);
                    if (relationships is { Count: > 0 })
                    {
                        relatedByContribution[contribution] = relationships;
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
                        "Provider {ProviderType} failed to expand subjects for {Key}={Value}. " +
                        "The preview will omit the subjects",
                        provider.ProviderType,
                        externalIdKey,
                        externalId);
                }
            }
        }

        if (primary is null)
        {
            logger.LogInformation(
                "No provider resolved a preview for {Key}={Value} (content type {ContentType}). " +
                "The metadata service will return no preview",
                externalIdKey,
                externalId,
                contentType);
            return null;
        }

        return Project(primary, contributions, relatedByContribution, providerType, contentType, resolvedLevel);
    }

    private static async Task<ProviderSearchExecution> SearchProviderAsync(
        ILogger<MetadataSearchService> logger,
        ProviderSearchInvocation invocation,
        int maximumResults,
        string defaultLanguagePreference,
        IMemoryCache memoryCache,
        CancellationToken cancellationToken)
    {
        var cacheKey = (
            Service: nameof(MetadataSearchService),
            Kind: "search",
            invocation.Provider.ProviderType,
            invocation.ContentType,
            Query: JsonSerializer.Serialize(invocation.Query),
            MaximumResults: maximumResults,
            Language: defaultLanguagePreference);

        try
        {
            var result = await memoryCache.GetOrCreateAsync(
                cacheKey,
                async entry =>
                {
                    var searchResult = await invocation.Provider.AdvancedSearchAsync(
                        invocation.ContentType,
                        invocation.Query,
                        maximumResults,
                        defaultLanguagePreference,
                        cancellationToken).ConfigureAwait(false);
                    entry.Size = Math.Max(1, searchResult.Matches.Count);
                    return searchResult;
                }).ConfigureAwait(false);

            return new ProviderSearchExecution(invocation, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Metadata provider {Provider} failed to search for {ContentType}. " +
                "This provider will contribute no candidates to the merged results",
                invocation.Provider.ProviderType,
                invocation.ContentType);
            return new ProviderSearchExecution(
                invocation,
                null,
                ProviderSearchFailedWarning(invocation.Provider.ProviderType));
        }
    }

    private static Image? SelectPrimaryImage(IEnumerable<MetadataContribution>? contributions)
    {
        if (contributions is null)
        {
            return null;
        }

        var ordered = contributions.OrderBy(c => c.Ordinal).ToList();

        var marked = (from c in ordered from i in c.Images where i.IsPrimary orderby c.Ordinal, i.Ordinal select i)
            .FirstOrDefault();
        if (marked is not null)
        {
            return marked;
        }

        foreach (var contribution in ordered)
        {
            var byOrdinal = contribution.Images
                .OrderBy(i => i.Ordinal)
                .FirstOrDefault();
            if (byOrdinal is not null)
            {
                return byOrdinal;
            }
        }

        return null;
    }

    private static void AddWarning(
        string? warning,
        HashSet<string> seen,
        List<string> warnings)
    {
        if (warning is not null && seen.Add(warning))
        {
            warnings.Add(warning);
        }
    }

    private static string ProviderSearchFailedWarning(string? providerType) =>
        $"{NormalizedProvider(providerType)} could not finish the search. Try again.";

    private static string ProviderWarning(string? providerType, string warning) =>
        $"{NormalizedProvider(providerType)}: {warning}";

    private static string NormalizedProvider(string? providerType) =>
        string.IsNullOrWhiteSpace(providerType) ? "unknown" : providerType.Trim();

    private static bool HasIdentity(MetadataSearchCandidate? candidate)
    {
        return candidate is not null &&
            !string.IsNullOrWhiteSpace(candidate.ProviderType) &&
            !string.IsNullOrWhiteSpace(candidate.ExternalIdSource) &&
            !string.IsNullOrWhiteSpace(candidate.ExternalId);
    }

    private static string? ResolveContentType(ContentFilterCriteria criteria)
    {
        foreach (var entry in criteria.ContentHierarchyItem.And)
        {
            foreach (var c in entry.ContentType.And)
            {
                if (c.Operator == CriteriaValueOperator.Equals && !string.IsNullOrWhiteSpace(c.Value))
                {
                    return c.Value;
                }

                if (c.Operator == CriteriaValueOperator.In && c.Values is not null)
                {
                    var first = c.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                    if (first is not null)
                    {
                        return first;
                    }
                }
            }
        }

        return null;
    }

    private static ContentHierarchyItemCriteria ProviderQuery(ContentFilterCriteria criteria, string contentType)
    {
        var item = criteria.ContentHierarchyItem.And.FirstOrDefault() ?? new ContentHierarchyItemCriteria();
        return item with
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    .. item.ContentType.And,
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Value = contentType
                    }
                ]
            }
        };
    }

    private static IReadOnlyList<ContentHierarchyLevelDefinition> MatchingDeclaredLevels(
        IReadOnlyList<ContentHierarchyLevelDefinition> levels,
        CriteriaSet<PropertyCriterion<string>> criteria)
    {
        var levelCriteria = criteria.And.ToArray();
        return [.. levels.Where(level => levelCriteria.All(criterion =>
            MatchesString(level.Slug, criterion) == MatchOutcome.Matched))];
    }

    private static IReadOnlyList<MetadataSearchCandidate> RankByTitleQuery(
        string? titleQuery,
        IReadOnlyList<IReadOnlyList<MetadataSearchCandidate>> groups)
    {
        return Rank(
            titleQuery ?? string.Empty,
            groups,
            candidate => candidate.Title,
            candidate => candidate.Year ?? candidate.ReleaseDate?.Year);
    }

    private static PreparedSearch PrepareSearch(ContentFilterCriteria criteria)
    {
        var merged = MergeMetadataCriteria(criteria);
        var titleCriterion = merged.Title.And.FirstOrDefault(criterion =>
        {
            var interpreted = InterpretOperator(criterion.Operator, criterion.Negate);
            return !interpreted.Negate && interpreted.Operator is
                CriteriaValueOperator.Equals or
                CriteriaValueOperator.ContainsString or
                CriteriaValueOperator.StartsWithString or
                CriteriaValueOperator.EndsWithString or
                CriteriaValueOperator.In or
                CriteriaValueOperator.ContainsWord;
        });
        var titleQuery = titleCriterion is null ? null : ReadValues(titleCriterion).FirstOrDefault();
        return new PreparedSearch(merged, titleQuery);
    }

    private static MetadataCriteria MergeMetadataCriteria(ContentFilterCriteria criteria)
    {
        var sources = criteria.ContentHierarchyItem.And
            .SelectMany(hierarchy => hierarchy.Metadata.And)
            .ToArray();
        if (sources.Length == 1)
        {
            return sources[0];
        }

        var merged = new MetadataCriteria();
        foreach (var source in sources)
        {
            merged = merged with
            {
                ProviderType = MergeSets(merged.ProviderType, source.ProviderType),
                Source = MergeSets(merged.Source, source.Source),
                Title = MergeSets(merged.Title, source.Title),
                OriginalTitle = MergeSets(merged.OriginalTitle, source.OriginalTitle),
                Language = MergeSets(merged.Language, source.Language),
                Overview = MergeSets(merged.Overview, source.Overview),
                PublishDate = MergeSets(merged.PublishDate, source.PublishDate),
                Duration = MergeSets(merged.Duration, source.Duration),
                ProductionStatus = MergeSets(merged.ProductionStatus, source.ProductionStatus),
                Established = MergeSets(merged.Established, source.Established),
                Dissolved = MergeSets(merged.Dissolved, source.Dissolved),
                BirthDate = MergeSets(merged.BirthDate, source.BirthDate),
                DeathDate = MergeSets(merged.DeathDate, source.DeathDate),
                CustomMetadata = MergeSets(merged.CustomMetadata, source.CustomMetadata),
                Awards = MergeCollections(merged.Awards, source.Awards),
                ExternalIds = MergeCollections(merged.ExternalIds, source.ExternalIds),
                References = MergeCollections(merged.References, source.References),
                Relationships = MergeCollections(merged.Relationships, source.Relationships),
                Images = MergeCollections(merged.Images, source.Images),
                Previews = MergeCollections(merged.Previews, source.Previews),
                AlternateTexts = MergeCollections(merged.AlternateTexts, source.AlternateTexts),
                ContentRatings = MergeCollections(merged.ContentRatings, source.ContentRatings),
                Genres = MergeCollections(merged.Genres, source.Genres),
                Tags = MergeCollections(merged.Tags, source.Tags),
                PreferredLanguage = merged.PreferredLanguage ?? source.PreferredLanguage
            };
        }

        return merged;
    }

    private static CriteriaSet<T> MergeSets<T>(CriteriaSet<T> left, CriteriaSet<T> right) =>
        new() { And = [.. left.And, .. right.And] };

    private static CollectionCriteria<T> MergeCollections<T>(CollectionCriteria<T> left, CollectionCriteria<T> right) =>
        new()
        {
            Some = [.. left.Some, .. right.Some],
            None = [.. left.None, .. right.None]
        };

    private static IReadOnlyList<MetadataSearchCandidate> FilterCandidatesAgainstCriteria(
        IReadOnlyList<MetadataSearchCandidate> candidates,
        MetadataCriteria criteria,
        bool preHydrationOnly = false)
    {
        var accepted = new List<MetadataSearchCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var violatesPreHydrationCriteria = criteria.Title.And.Any(criterion =>
                MatchesString(candidate.Title, criterion) == MatchOutcome.Violated) ||
                criteria.Language.And.Any(criterion =>
                    MatchesString(candidate.Language, criterion) == MatchOutcome.Violated) ||
                criteria.Overview.And.Any(criterion =>
                    MatchesString(candidate.Overview, criterion) == MatchOutcome.Violated);
            if (violatesPreHydrationCriteria)
            {
                continue;
            }

            var violatesHydratedCriteria = !preHydrationOnly &&
                (criteria.ProviderType.And.Any(criterion =>
                    MatchesString(candidate.ProviderType, criterion) == MatchOutcome.Violated) ||
                    criteria.Source.And.Any(criterion =>
                        MatchesString(
                            candidate.ExternalIdSource,
                            criterion,
                            name => name.NormalizeExternalIdentifierName()) == MatchOutcome.Violated) ||
                    criteria.PublishDate.And.Any(criterion =>
                        MatchesPublishDate(candidate, criterion) == MatchOutcome.Violated) ||
                    criteria.ExternalIds.Some.Any(group =>
                        VerifyExternalIdGroup(candidate, group) == MatchOutcome.Violated) ||
                    criteria.ExternalIds.None.Any(excluded =>
                        VerifyExternalIdExclusion(candidate, excluded) == MatchOutcome.Violated) ||
                    criteria.Relationships.Some.Any(group =>
                        VerifyRelationshipGroup(candidate, group) == MatchOutcome.Violated) ||
                    criteria.Relationships.None.Any(excluded =>
                        VerifyRelationshipExclusion(candidate, excluded) == MatchOutcome.Violated));
            if (!violatesHydratedCriteria)
            {
                accepted.Add(candidate);
            }
        }

        return accepted;
    }

    private static MatchOutcome MatchesString(
        string? actual,
        PropertyCriterion<string> criterion,
        Func<string, string>? normalize = null)
    {
        var interpreted = InterpretOperator(criterion.Operator, criterion.Negate);
        var values = ReadValues(criterion);
        return NegateOutcome(MatchesString(actual, interpreted.Operator, values, normalize), interpreted.Negate);
    }

    private static MatchOutcome MatchesString(
        string? actual,
        CriteriaValueOperator op,
        IReadOnlyList<string> values,
        Func<string, string>? normalize = null)
    {
        switch (op)
        {
            case CriteriaValueOperator.IsNull:
                return Outcome(string.IsNullOrEmpty(actual));
            case CriteriaValueOperator.IsNotNull:
                return Outcome(!string.IsNullOrEmpty(actual));
        }

        if (string.IsNullOrEmpty(actual))
        {
            return MatchOutcome.Unverifiable;
        }

        if (normalize is not null && op is
            CriteriaValueOperator.Equals or
            CriteriaValueOperator.NotEquals or
            CriteriaValueOperator.In or
            CriteriaValueOperator.NotIn)
        {
            actual = normalize(actual);
            values = [.. values.Select(normalize)];
        }

        var value = values.Count > 0 ? values[0] : null;
        return op switch
        {
            CriteriaValueOperator.Equals =>
                value is null
                    ? MatchOutcome.Unverifiable
                    : Outcome(string.Equals(actual, value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.In =>
                values.Count == 0
                    ? MatchOutcome.Unverifiable
                    : Outcome(values.Any(v => string.Equals(actual, v, StringComparison.OrdinalIgnoreCase))),
            CriteriaValueOperator.ContainsString =>
                value is null
                    ? MatchOutcome.Unverifiable
                    : Outcome(actual.Contains(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.StartsWithString =>
                value is null
                    ? MatchOutcome.Unverifiable
                    : Outcome(actual.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.EndsWithString =>
                value is null
                    ? MatchOutcome.Unverifiable
                    : Outcome(actual.EndsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsWord =>
                value is null ? MatchOutcome.Unverifiable : Outcome(ContainsWord(actual, value)),
            _ => MatchOutcome.Unverifiable
        };
    }

    private static MatchOutcome MatchesPublishDate(
        MetadataSearchCandidate candidate,
        PropertyCriterion<TemporalValue> criterion)
    {
        if (criterion.Operator is CriteriaValueOperator.IsNull)
        {
            return NegateOutcome(Outcome(candidate.ReleaseDate is null && candidate.Year is null), criterion.Negate);
        }

        if (criterion.Operator is CriteriaValueOperator.IsNotNull)
        {
            return NegateOutcome(
                Outcome(candidate.ReleaseDate is not null || candidate.Year is not null),
                criterion.Negate);
        }

        var requested = ReadValues(criterion);
        if (requested.Count == 0)
        {
            return MatchOutcome.Unverifiable;
        }

        var candidateValue = candidate.ReleaseDate is { } releaseDate
            ? new TemporalValue { Value = releaseDate, Precision = TemporalPrecision.Instant }
            : candidate.Year is { } year
                ? new TemporalValue
                {
                    Value = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Year
                }
                : null;
        if (candidateValue is null)
        {
            return MatchOutcome.Unverifiable;
        }

        if (criterion.Operator is not (CriteriaValueOperator.Equals
            or CriteriaValueOperator.NotEquals
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual
            or CriteriaValueOperator.In
            or CriteriaValueOperator.NotIn))
        {
            return MatchOutcome.Unverifiable;
        }

        return NegateOutcome(Outcome(candidateValue.Compare(criterion.Operator, requested)), criterion.Negate);
    }

    private static IReadOnlyList<T> ReadValues<T>(PropertyCriterion<T> criterion)
    {
        var values = new List<T>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value);
        }

        foreach (var value in criterion.Values ?? [])
        {
            if (value is not null)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static (CriteriaValueOperator Operator, bool Negate) InterpretOperator(
        CriteriaValueOperator op,
        bool negate)
    {
        var interpreted = op switch
        {
            CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
            CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
            CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
            CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
            CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
            _ => (op, false)
        };
        return (interpreted.Item1, negate ^ interpreted.Item2);
    }

    private static MatchOutcome VerifyExternalIdGroup(
        MetadataSearchCandidate candidate,
        IReadOnlyList<ExternalIdCriteria> alternatives)
    {
        if (string.IsNullOrWhiteSpace(candidate.ExternalId))
        {
            return MatchOutcome.Unverifiable;
        }

        return alternatives.Any(alt => MatchesExternalId(candidate, alt) == MatchOutcome.Matched)
            ? MatchOutcome.Matched
            : MatchOutcome.Unverifiable;
    }

    private static MatchOutcome VerifyExternalIdExclusion(
        MetadataSearchCandidate candidate,
        ExternalIdCriteria excluded)
    {
        if (string.IsNullOrWhiteSpace(candidate.ExternalId))
        {
            return MatchOutcome.Unverifiable;
        }

        return MatchesExternalId(candidate, excluded) == MatchOutcome.Matched
            ? MatchOutcome.Violated
            : MatchOutcome.Unverifiable;
    }

    private static MatchOutcome MatchesExternalId(
        MetadataSearchCandidate candidate,
        ExternalIdCriteria criteria)
    {
        var sourceOutcome = MatchesSet(
            candidate.ExternalIdSource,
            criteria.Source,
            name => name.NormalizeExternalIdentifierName());
        var valueOutcome = MatchesSet(
            candidate.ExternalId,
            criteria.Value,
            value => value.NormalizeExternalIdentifierValue());
        if (sourceOutcome == MatchOutcome.Matched && valueOutcome == MatchOutcome.Matched)
        {
            return MatchOutcome.Matched;
        }

        return sourceOutcome == MatchOutcome.Violated || valueOutcome == MatchOutcome.Violated
            ? MatchOutcome.Violated
            : MatchOutcome.Unverifiable;
    }

    private static MatchOutcome VerifyRelationshipGroup(
        MetadataSearchCandidate candidate,
        IReadOnlyList<RelationshipCriteria> alternatives)
    {
        var sawUnverifiable = false;
        foreach (var alternative in alternatives)
        {
            var outcome = VerifyRelationship(candidate, alternative);
            if (outcome == MatchOutcome.Matched)
            {
                return MatchOutcome.Matched;
            }

            if (outcome == MatchOutcome.Unverifiable)
            {
                sawUnverifiable = true;
            }
        }

        return sawUnverifiable ? MatchOutcome.Unverifiable : MatchOutcome.Violated;
    }

    private static MatchOutcome VerifyRelationshipExclusion(
        MetadataSearchCandidate candidate,
        RelationshipCriteria excluded)
    {
        var outcome = VerifyRelationship(candidate, excluded);
        return outcome switch
        {
            MatchOutcome.Matched => MatchOutcome.Violated,
            MatchOutcome.Violated => MatchOutcome.Matched,
            _ => MatchOutcome.Unverifiable
        };
    }

    private static MatchOutcome VerifyRelationship(
        MetadataSearchCandidate candidate,
        RelationshipCriteria relationship)
    {
        if (candidate.SubjectReferences is not { Count: > 0 })
        {
            return MatchOutcome.Unverifiable;
        }

        var sawUnverifiable = false;
        foreach (var reference in candidate.SubjectReferences)
        {
            var outcome = MatchesRelationshipReference(reference, relationship);
            if (outcome == MatchOutcome.Matched)
            {
                return MatchOutcome.Matched;
            }

            if (outcome == MatchOutcome.Unverifiable)
            {
                sawUnverifiable = true;
            }
        }

        return sawUnverifiable ? MatchOutcome.Unverifiable : MatchOutcome.Violated;
    }

    private static MatchOutcome MatchesRelationshipReference(
        ProviderMetadataSearchSubjectReference reference,
        RelationshipCriteria relationship)
    {
        var facets = new[]
        {
            MatchesSetAny(relationship.Kind, reference.SubjectKind, reference.RelationshipKind),
            MatchesSetAny(relationship.Type, reference.RelationshipKind, reference.SubjectKind),
            MatchesSet(reference.RelationshipQualifier, relationship.Qualifier),
            MatchesSet(reference.DisplayName, relationship.Subject),
            relationship.RelatedMetadataContainerId.And.Any()
                ? MatchOutcome.Unverifiable
                : MatchOutcome.Matched,
            MatchesRelationshipExternalIds(reference, relationship.ExternalIds)
        };
        if (facets.Any(f => f == MatchOutcome.Violated))
        {
            return MatchOutcome.Violated;
        }

        return facets.Any(f => f == MatchOutcome.Unverifiable)
            ? MatchOutcome.Unverifiable
            : MatchOutcome.Matched;
    }

    private static MatchOutcome MatchesRelationshipExternalIds(
        ProviderMetadataSearchSubjectReference reference,
        CollectionCriteria<ExternalIdCriteria> criteria)
    {
        if (!criteria.Some.Any() && !criteria.None.Any())
        {
            return MatchOutcome.Matched;
        }

        if (reference.ExternalIds is not { Count: > 0 })
        {
            return MatchOutcome.Unverifiable;
        }

        foreach (var group in criteria.Some)
        {
            var groupMatched = group.Any(alternative => reference.ExternalIds.Any(pair =>
                MatchesSet(
                    pair.Key,
                    alternative.Source,
                    name => name.NormalizeExternalIdentifierName()) == MatchOutcome.Matched &&
                MatchesSet(
                    pair.Value,
                    alternative.Value,
                    value => value.NormalizeExternalIdentifierValue()) == MatchOutcome.Matched));
            if (!groupMatched)
            {
                return MatchOutcome.Violated;
            }
        }

        foreach (var excluded in criteria.None)
        {
            var matched = reference.ExternalIds.Any(pair =>
                MatchesSet(
                    pair.Key,
                    excluded.Source,
                    name => name.NormalizeExternalIdentifierName()) == MatchOutcome.Matched &&
                MatchesSet(
                    pair.Value,
                    excluded.Value,
                    value => value.NormalizeExternalIdentifierValue()) == MatchOutcome.Matched);
            if (matched)
            {
                return MatchOutcome.Violated;
            }
        }

        return MatchOutcome.Matched;
    }

    private static MatchOutcome MatchesSet(
        string? actual,
        CriteriaSet<PropertyCriterion<string>> set,
        Func<string, string>? normalize = null)
    {
        var sawUnverifiable = false;
        foreach (var criterion in set.And)
        {
            var outcome = MatchesString(actual, criterion, normalize);
            if (outcome == MatchOutcome.Violated)
            {
                return MatchOutcome.Violated;
            }

            if (outcome == MatchOutcome.Unverifiable)
            {
                sawUnverifiable = true;
            }
        }

        return sawUnverifiable ? MatchOutcome.Unverifiable : MatchOutcome.Matched;
    }

    private static MatchOutcome MatchesSetAny(CriteriaSet<PropertyCriterion<string>> set, params string?[] actualValues)
    {
        if (!set.And.Any())
        {
            return MatchOutcome.Matched;
        }

        var outcomes = actualValues.Select(actual => MatchesSet(actual, set)).ToArray();
        if (outcomes.Any(o => o == MatchOutcome.Matched))
        {
            return MatchOutcome.Matched;
        }

        return outcomes.Any(o => o == MatchOutcome.Unverifiable)
            ? MatchOutcome.Unverifiable
            : MatchOutcome.Violated;
    }

    private static bool ContainsWord(string actual, string word)
    {
        var index = actual.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(actual[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= actual.Length || !char.IsLetterOrDigit(actual[afterIndex]);
            if (before && after)
            {
                return true;
            }

            index = actual.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static MatchOutcome NegateOutcome(MatchOutcome outcome, bool negated)
    {
        if (!negated)
        {
            return outcome;
        }

        return outcome switch
        {
            MatchOutcome.Matched => MatchOutcome.Violated,
            MatchOutcome.Violated => MatchOutcome.Matched,
            _ => MatchOutcome.Unverifiable
        };
    }

    private static MatchOutcome Outcome(bool matched) => matched ? MatchOutcome.Matched : MatchOutcome.Violated;

    private static bool SupportsContentType(IEnumerable<string>? supportedContentTypes, string contentType) =>
        supportedContentTypes is not null &&
        supportedContentTypes.Any(supported => supported == "*" || supported == contentType);

    private static IReadOnlyList<T> Rank<T>(
        string query,
        IReadOnlyList<IReadOnlyList<T>> groups,
        Func<T, string?> titleSelector,
        Func<T, int?> yearSelector)
    {
        var (titleQuery, queryYear) = SplitQuery(query);
        var unranked = titleQuery.Length == 0;
        return
        [
            .. from provider in groups.Select((items, providerOrdinal) => (items, providerOrdinal))
                from entry in provider.items.Select((item, rankInProvider) => (item, rankInProvider))
                orderby unranked ? TierExactTitle : TitleTier(titleQuery, titleSelector(entry.item)),
                    unranked ? YearNeutral : YearDistance(queryYear, yearSelector(entry.item)),
                    entry.rankInProvider,
                    provider.providerOrdinal
                select entry.item
        ];
    }

    private static int TitleTier(string titleQuery, string? title)
    {
        var normalizedTitle = title.NormalizeContentSearchPhrase();
        if (normalizedTitle.Length == 0)
        {
            return TierTitleUnrelated;
        }

        if (string.Equals(normalizedTitle, titleQuery, StringComparison.Ordinal))
        {
            return TierExactTitle;
        }

        if (normalizedTitle.StartsWith(titleQuery, StringComparison.Ordinal))
        {
            return TierTitlePrefix;
        }

        return normalizedTitle.Contains(titleQuery, StringComparison.Ordinal)
            ? TierTitleContains
            : TierTitleUnrelated;
    }

    private static int YearDistance(int? queryYear, int? itemYear)
    {
        if (queryYear is null)
        {
            return YearNeutral;
        }

        return itemYear is null
            ? YearMissingPenalty
            : Math.Abs(itemYear.Value - queryYear.Value);
    }

    private static (string TitleQuery, int? Year) SplitQuery(string query)
    {
        var normalized = query.NormalizeContentSearchPhrase();
        if (normalized.Length == 0)
        {
            return (normalized, null);
        }

        var tokens = normalized.Split(' ');
        if (tokens.Length < 2)
        {
            return (normalized, null);
        }

        int? year = null;
        var remaining = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (year is null && TryParseYear(token, out var parsed))
            {
                year = parsed;
                continue;
            }

            remaining.Add(token);
        }

        return year is null || remaining.Count == 0
            ? (normalized, null)
            : (string.Join(' ', remaining), year);
    }

    private static bool TryParseYear(string token, out int year)
    {
        year = 0;
        if (token.Length != 4 ||
            !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value is < MinPlausibleYear or > MaxPlausibleYear)
        {
            return false;
        }

        year = value;
        return true;
    }

    private static ContentPreview Project(
        MetadataContribution primary,
        IReadOnlyList<MetadataContribution> contributions,
        IReadOnlyDictionary<MetadataContribution, IReadOnlyList<SubjectRelationshipDefinition>> relatedByContribution,
        string providerType,
        string contentType,
        string levelSlug)
    {
        var genres = (
            from contribution in contributions
            from genre in contribution.Genres.OrderBy(genre => genre.Ordinal)
            where !string.IsNullOrWhiteSpace(genre.Name)
            select genre.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ratings = contributions
            .SelectMany(c => c.ContentRatings.OrderBy(r => r.Ordinal))
            .Select(r => new ContentPreviewRating { Body = r.Body, Rating = r.Rating })
            .ToList();

        var images = contributions
            .SelectMany(c => c.Images.OrderBy(i => i.Ordinal))
            .Select(i => new ContentPreviewImage { Type = i.Type, Url = i.Url.AbsoluteUri, Language = i.Language, })
            .ToList();

        var previews = contributions
            .SelectMany(c => c.Previews.OrderBy(p => p.Ordinal))
            .Select(p => new ContentPreviewPreview
            {
                Kind = p.Kind,
                EmbedKind = p.EmbedKind,
                Url = p.Url.AbsoluteUri,
                Title = p.Title,
                Language = p.Language,
            })
            .ToList();

        var externalIds = contributions
            .SelectMany(c => c.ExternalIds.OrderBy(e => e.Ordinal))
            .Select(e => new ContentPreviewExternalId { Source = e.Source, Value = e.Value })
            .ToList();

        var relatedSubjects = new List<ContentPreviewRelatedSubject>();
        foreach (var contribution in contributions)
        {
            if (!relatedByContribution.TryGetValue(contribution, out var definitions))
            {
                continue;
            }

            foreach (var definition in definitions.OrderBy(d => d.RelationshipOrdinal))
            {
                if (definition is null || string.IsNullOrWhiteSpace(definition.SubjectName))
                {
                    continue;
                }

                relatedSubjects.Add(new ContentPreviewRelatedSubject
                {
                    Name = definition.SubjectName,
                    Kind = definition.SubjectKind,
                    RelationshipKind = definition.RelationshipKind,
                    RelationshipQualifier = definition.RelationshipQualifier,
                    Ordinal = definition.RelationshipOrdinal,
                    ImageUrl = ResolveSeedImageUrl(definition.InitialSubjectContribution),
                });
            }
        }

        return new ContentPreview
        {
            ProviderType = string.IsNullOrWhiteSpace(primary.ProviderType) ? providerType : primary.ProviderType,
            ContentType = contentType,
            LevelSlug = levelSlug,
            Title = primary.Title,
            OriginalTitle = primary.OriginalTitle,
            Overview = primary.Overview,
            ReleaseDate = primary.PublishDate,
            Year = primary.PublishDate?.Year,
            Duration = primary.Duration,
            Language = primary.Language,
            ProductionStatus = primary.ProductionStatus,
            Genres = genres,
            ContentRatings = ratings,
            Images = images,
            Previews = previews,
            ExternalIds = externalIds,
            RelatedSubjects = relatedSubjects,
        };
    }

    private static string? ResolveSeedImageUrl(MetadataContribution? seed)
    {
        if (seed is null || seed.Images.Count == 0)
        {
            return null;
        }

        return SelectPrimaryImage([seed])?.Url.AbsoluteUri;
    }

    private async Task<IReadOnlyList<MetadataSearchCandidate>> PrepareCandidatesAsync(
        IContentHierarchyMetadataAdvancedSearcher provider,
        IReadOnlyList<MetadataSearchCandidate> candidates,
        PreparedSearch prepared,
        string defaultLanguagePreference,
        HashSet<(string, string)> inLibraryCandidateKeys,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var existingCandidateKeys = await FindExistingCandidateKeysAsync(candidates, cancellationToken)
            .ConfigureAwait(false);
        inLibraryCandidateKeys.UnionWith(existingCandidateKeys);
        var hydrated = new MetadataSearchCandidate?[candidates.Count];
        await Task.WhenAll(
            Enumerable.Range(0, candidates.Count).Select(
            async index =>
            {
                var candidate = candidates[index];
                var candidateKey = (
                    candidate.ExternalIdSource.ToLowerInvariant(),
                    candidate.ExternalId.ToLowerInvariant());
                if (existingCandidateKeys.Contains(candidateKey))
                {
                    hydrated[index] = candidate;
                    return;
                }

                if (FilterCandidatesAgainstCriteria(
                    [candidate],
                    prepared.Criteria,
                    preHydrationOnly: true).Count == 0)
                {
                    return;
                }

                var cacheKey = (
                    Service: nameof(MetadataSearchService),
                    Kind: "hydrate",
                    provider.ProviderType,
                    candidate.ContentType,
                    Source: candidate.ExternalIdSource,
                    candidate.ExternalId,
                    Language: defaultLanguagePreference);
                var hydratedCandidate = await memoryCache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        entry.Size = 1;
                        return await provider
                            .HydrateAsync(candidate, defaultLanguagePreference, cancellationToken)
                            .ConfigureAwait(false);
                    }).ConfigureAwait(false);

                hydrated[index] = hydratedCandidate is null
                    ? candidate
                    : hydratedCandidate with
                    {
                        SubjectReferences = candidate.SubjectReferences is { Count: > 0 }
                            ? candidate.SubjectReferences
                            : hydratedCandidate.SubjectReferences
                    };
            })).ConfigureAwait(false);

        return [.. hydrated.Where(candidate => candidate is not null).Select(candidate => candidate!)];
    }

    private async Task<HashSet<(string, string)>> FindExistingCandidateKeysAsync(
        IReadOnlyList<MetadataSearchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var idValues = candidates
            .Select(c => c.ExternalId.ToLowerInvariant())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .ToArray();
        if (idValues.Length == 0)
        {
            return [];
        }

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        ExternalIds = new()
                        {
                            Some =
                            [
                                [.. idValues.Select(value => new ExternalIdCriteria
                                {
                                    Value = new() { And = [new() { Value = value }] }
                                })]
                            ]
                        }
                    }
                ]
            }
        };
        var pageSize = Math.Max(1, idValues.Length);
        var pageNumber = 1;
        var existingItems = new List<ContentHierarchyItem>();
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                criteria,
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken)
                .ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            existingItems.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        if (existingItems.Count == 0)
        {
            return [];
        }

        var existingCandidateKeys = new HashSet<(string, string)>();
        foreach (var item in existingItems)
        {
            foreach (var externalId in item.MetadataContainer?.Contributions.SelectMany(c => c.ExternalIds) ?? [])
            {
                var key = ((externalId.Source ?? string.Empty).ToLowerInvariant(), externalId.Value.ToLowerInvariant());
                existingCandidateKeys.Add(key);
            }
        }

        return existingCandidateKeys;
    }

    private sealed record PreparedSearch(
        MetadataCriteria Criteria,
        string? TitleQuery);

    private sealed record ProviderSearchInvocation(
        string ContentType,
        IContentHierarchyMetadataAdvancedSearcher Provider,
        ContentHierarchyItemCriteria Query);

    private sealed record ProviderSearchExecution(
        ProviderSearchInvocation Invocation,
        MetadataSearchPage? Result,
        string? Warning);
}
