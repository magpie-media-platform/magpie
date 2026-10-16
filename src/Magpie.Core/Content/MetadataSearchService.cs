using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.QueryLanguage;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Content;

internal sealed class MetadataSearchService(
    IEnumerable<IContentHierarchyMetadataProvider> metadataProviders,
    IEnumerable<IContentHierarchyMetadataSearcher> searchers,
    IContentTypeRegistry contentTypeRegistry,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    TimeProvider timeProvider,
    ILogger<MetadataSearchService> logger,
    ICacheFactory cacheFactory) : IMetadataSearchService
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

    private const int PageFillRounds = 4;

    private static readonly TimeSpan PageMemoRetention = TimeSpan.FromHours(12);

    private static readonly TimeSpan HydrationRetention = TimeSpan.FromMinutes(15);

    private const int ProviderFailureThreshold = 3;

    private static readonly TimeSpan ProviderFailureCooldown = TimeSpan.FromMinutes(2);

    private readonly ICache _providerHealthCache = cacheFactory.GetOrCreate(
        "metadata-provider-health",
        () => new CachePolicy
        {
            Freshness = ProviderFailureCooldown,
            Retention = ProviderFailureCooldown,
            MaximumEntries = 64,
        });

    private readonly ICache _hydrationCache = cacheFactory.GetOrCreate(
        "metadata-hydration",
        () => new CachePolicy
        {
            Freshness = HydrationRetention,
            Retention = HydrationRetention,
            MaximumEntries = 10_000,
        });

    private readonly ICache _pageMemoCache = cacheFactory.GetOrCreate(
        "metadata-search-pages",
        () => new CachePolicy
        {
            Freshness = PageMemoRetention,
            Retention = PageMemoRetention,
            MaximumEntries = 1_000,
        });

    private enum MatchOutcome
    {
        Matched,

        Violated,

        Unverifiable
    }

    /// <inheritdoc />
    public Task<ProviderMetadataCandidateSearchResult> SearchCandidatesAsync(
        ContentFilterCriteria criteria,
        string? contentType,
        int page,
        CancellationToken cancellationToken = default) =>
        SearchCandidatesAsync(criteria, contentType, page, null, cancellationToken);

    /// <inheritdoc />
    public async Task<ProviderMetadataCandidateSearchResult> SearchCandidatesAsync(
        ContentFilterCriteria criteria,
        string? contentType,
        int page,
        string? continuation,
        CancellationToken cancellationToken = default)
    {
        using var activity = ContentTelemetry.Source.StartActivity("magpie.metadata.search");
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("magpie.query.mql", criteria.FormatContentFilterOrDefault());
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            return await SearchCandidatesCoreAsync(
                criteria,
                contentType,
                page,
                continuation,
                activity,
                cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ContentTelemetry.SearchDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task<ProviderMetadataCandidateSearchResult> SearchCandidatesCoreAsync(
        ContentFilterCriteria criteria,
        string? contentType,
        int page,
        string? continuation,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        if (continuation is null && page > 1)
        {
            throw new MetadataSearchContinuationException("A continuation is required for a page after page one.");
        }

        var requestIdentity = JsonSerializer.Serialize(new { criteria, contentType });
        SearchCheckpoint? previous = null;
        if (continuation is not null)
        {
            if (!_pageMemoCache.TryGet(continuation, out previous) || previous is null)
            {
                throw new MetadataSearchContinuationException("The search checkpoint expired or is invalid.");
            }

            if (previous.RequestIdentity != requestIdentity || previous.Page != page)
            {
                throw new MetadataSearchContinuationException("The search checkpoint does not match this request.");
            }
        }

        var resolvedContentTypes = !string.IsNullOrWhiteSpace(contentType)
            ? (IReadOnlyList<string>)[contentType.Trim()]
            : ResolveContentTypes(criteria);

        IReadOnlyList<string> contentTypes;
        if (resolvedContentTypes is not null)
        {
            if (resolvedContentTypes.Count == 0)
            {
                return ProviderMetadataCandidateSearchResult.Empty;
            }

            contentTypes = resolvedContentTypes;
        }
        else
        {
            contentTypes = [.. (
                from d in contentTypeRegistry.Definitions
                where !string.IsNullOrWhiteSpace(d.Slug)
                select d.Slug).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (contentTypes.Count == 0)
            {
                if (previous is not null)
                {
                    throw new MetadataSearchContinuationException("The content type registry changed.");
                }

                logger.LogInformation(
                    "The metadata service skipped the provider candidate search because no content types " +
                    "are registered");
                return ProviderMetadataCandidateSearchResult.Empty;
            }

            logger.LogInformation(
                "The metadata candidate search has no content type constraint and searches all {Count} types",
                contentTypes.Count);
        }

        activity?.SetTag("magpie.search.content_types", string.Join(",", contentTypes));

        var warningsSeen = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var prepared = PrepareSearch(criteria);
        const string defaultLanguagePreference = "";
        var requestedPage = Math.Max(1, page);
        activity?.SetTag("magpie.search.requested_page", requestedPage);
        var existingLibraryItems = new Dictionary<(string, string, string, string), Guid>();
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
                    (
                        type,
                        query with
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

        foreach (var rejectedQuery in rejectedQueries)
        {
            logger.LogInformation(
                "Skipped {ContentType} for an invalid level filter; valid levels are {ValidLevels}",
                rejectedQuery.ContentType,
                string.Join(", ", rejectedQuery.Levels.Select(level => level.Slug)));
        }

        var invocations = new List<ProviderSearchInvocation>();
        foreach (var candidate in
            from validQuery in validQueries
            from provider in searchers.OfType<IContentHierarchyMetadataAdvancedSearcher>()
            where SupportsContentType(provider.SupportedContentTypes, validQuery.ContentType)
            select new ProviderSearchInvocation(validQuery.ContentType, provider, validQuery.Query))
        {
            invocations.Add(candidate);
        }

        var providersQueried = invocations.Count;
        activity?.SetTag("magpie.search.providers_queried", providersQueried);
        if (providersQueried == 0)
        {
            if (previous is not null)
            {
                throw new MetadataSearchContinuationException("The search providers changed.");
            }

            if (validQueries.Count > 0)
            {
                logger.LogInformation(
                    "The metadata search found no providers for types [{ContentTypes}]",
                    string.Join(", ", validQueries.Select(query => query.ContentType)));
            }

            return ProviderMetadataCandidateSearchResult.Empty with { Warnings = warnings };
        }

        var fingerprint = JsonSerializer.Serialize(new
        {
            Criteria = criteria,
            ContentType = contentType,
            Providers = invocations.Select(invocation => new
            {
                invocation.Provider.ProviderType,
                Implementation = invocation.Provider.GetType().AssemblyQualifiedName,
                invocation.ContentType,
                invocation.Query
            }),
            Language = defaultLanguagePreference
        });
        SearchCheckpoint checkpoint;
        if (continuation is null)
        {
            if (requestedPage != 1)
            {
                throw new MetadataSearchContinuationException("A continuation is required for a page after page one.");
            }

            checkpoint = new(
                fingerprint,
                requestIdentity,
                1,
                [],
                [.. invocations.Select(_ =>
                    new ProviderProgress(null, false, []))]);
            continuation = Guid.NewGuid().ToString("N");
            _pageMemoCache.Set(continuation, checkpoint);
        }
        else
        {
            checkpoint = previous!;
            if (checkpoint.Fingerprint != fingerprint || checkpoint.Page != requestedPage)
            {
                throw new MetadataSearchContinuationException("The search checkpoint does not match this request.");
            }
        }

        var current = await ReadPageAsync(
            checkpoint,
            invocations,
            prepared,
            defaultLanguagePreference,
            existingLibraryItems,
            warningsSeen,
            warnings,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var nextContinuation = current.HasMore ? Guid.NewGuid().ToString("N") : null;
        if (nextContinuation is not null)
        {
            _pageMemoCache.Set(nextContinuation, current.Next);
        }

        var pageWindow = current.Rows;
        var pageMatches = RankByTitleQuery(prepared.TitleQuery, [pageWindow]).ToList();
        var matches = pageMatches.Select(candidate =>
        {
            var key = CandidateLibraryIdentity(candidate);
            var inLibrary = existingLibraryItems.TryGetValue(key, out var existingContentHierarchyItemId);
            return new MetadataSearchMatch
            {
                Candidate = candidate,
                InLibrary = inLibrary,
                ExistingContentHierarchyItemId = inLibrary ? existingContentHierarchyItemId : null
            };
        }).ToArray();
        var ownedCount = matches.Count(match => match.InLibrary);
        activity?.SetTag("magpie.search.merged_count", current.MergedCount);
        activity?.SetTag("magpie.search.owned_count", ownedCount);
        logger.LogInformation(
            "Merged {Merged} results from {Providers} providers; {Owned} returned results are in the library",
            current.MergedCount,
            providersQueried,
            ownedCount);

        return new ProviderMetadataCandidateSearchResult
        {
            Continuation = continuation,
            NextContinuation = nextContinuation,
            Matches = matches,
            PageItemCount = pageMatches.Count,
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = requestedPage,
                HasMore = current.HasMore
            },
            Warnings = warnings
        };
    }

    public async Task<MetadataSearchPage> SearchAsync(
        string query,
        string contentType,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(contentType))
        {
            return MetadataSearchPage.NotSupported;
        }

        var matchingSearchers = searchers
            .Where(searcher => SupportsContentType(searcher.SupportedContentTypes, contentType))
            .ToArray();
        if (matchingSearchers.Length == 0)
        {
            return MetadataSearchPage.NotSupported;
        }

        var requestedPage = Math.Max(1, page);
        var tasks = contentTypeRegistry.GetRootLevels(contentType)
            .SelectMany(level => matchingSearchers.Select(searcher => SearchOneAsync(
                searcher,
                level,
                query,
                requestedPage,
                cancellationToken)))
            .ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        if (results.Length > 0 && results.All(result => result.Warning is not null))
        {
            throw new InvalidOperationException(
                "Metadata search failed; no provider answered successfully. " +
                string.Join(" ", results.Select(result => result.Warning).Distinct(StringComparer.Ordinal)));
        }

        var deduplicated = DeduplicateAcrossProviders([.. results.Select(result => result.Matches)]);
        return new MetadataSearchPage
        {
            Supported = true,
            Matches = Rank(query, deduplicated, item => item.Title, item => item.Year ?? item.ReleaseDate?.Year),
            PageInfo = new ProviderSearchPageInfo { RequestedPage = requestedPage, HasMore = false },
            Warnings = [.. results
                .Select(result => result.Warning)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)]
        };
    }

    private async Task<(IReadOnlyList<MetadataSearchCandidate> Matches, string? Warning)> SearchOneAsync(
        IContentHierarchyMetadataSearcher searcher,
        ContentHierarchyLevelDefinition level,
        string query,
        int page,
        CancellationToken cancellationToken)
    {
        IEnumerable<MetadataSearchCandidate> results;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            results = await searcher
                .SearchAsync(level, query, defaultLanguagePreference: string.Empty, page, cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "An operation timed out while metadata provider {Provider} searched for {Query}",
                searcher.ProviderType,
                query);
            return ([], ProviderSearchTimedOutWarning(searcher.ProviderType));
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Metadata provider {Provider} failed the search for {Query}",
                searcher.ProviderType,
                query);
            return ([], ProviderSearchFailedWarning(searcher.ProviderType));
        }

        return (
            [.. results.Select(item => string.IsNullOrWhiteSpace(item.Level)
                ? item with { Level = level.Slug }
                : item)],
            null);
    }

    private async Task<ContinuedPage> ReadPageAsync(
        SearchCheckpoint checkpoint,
        IReadOnlyList<ProviderSearchInvocation> invocations,
        PreparedSearch prepared,
        string language,
        Dictionary<(string, string, string, string), Guid> existingLibraryItems,
        HashSet<string> warningsSeen,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var progress = checkpoint.Providers.ToArray();
        var retained = progress.SelectMany(item => item.Pending).ToArray();
        foreach (var existing in await FindExistingCandidatesAsync(retained, cancellationToken).ConfigureAwait(false))
        {
            existingLibraryItems[existing.Key] = existing.Value;
        }

        var failed = new HashSet<int>();
        var requestFailed = false;
        var answered = false;
        var validationIndexes = Enumerable.Range(0, progress.Length)
            .Where(index => progress[index].Continuation is not null).ToArray();
        var validations = await Task.WhenAll(validationIndexes.Select(index => SearchProviderAsync(
            logger,
            invocations[index],
            0,
            language,
            progress[index].Continuation,
            _providerHealthCache,
            recordHealth: false,
            cancellationToken))).ConfigureAwait(false);
        for (var index = 0; index < validations.Length; index++)
        {
            var validation = validations[index];
            requestFailed |= validation.Warning is not null;
            AddWarning(validation.Warning, warningsSeen, warnings);
            foreach (var warning in validation.Result?.Page.Warnings ?? [])
            {
                AddWarning(
                    ProviderWarning(validation.Invocation.Provider.ProviderType, warning),
                    warningsSeen,
                    warnings);
            }

            if (validation.Warning is not null || validation.Result?.Page.Warnings.Count > 0)
            {
                failed.Add(validationIndexes[index]);
            }
        }

        List<MetadataSearchCandidate> rows = [];
        for (var round = 0; round < PageFillRounds; round++)
        {
            var indexes = Enumerable.Range(0, progress.Length)
                .Where(index => !progress[index].Exhausted &&
                    progress[index].Pending.Count < ProviderSearchPageSize &&
                    !failed.Contains(index))
                .ToArray();
            var results = await Task.WhenAll(indexes.Select(index => SearchProviderAsync(
                logger,
                invocations[index],
                ProviderSearchPageSize,
                language,
                progress[index].Continuation,
                _providerHealthCache,
                recordHealth: round == 0,
                cancellationToken))).ConfigureAwait(false);
            for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
            {
                var index = indexes[resultIndex];
                var execution = results[resultIndex];
                if (execution.Warning is not null || execution.Result is null)
                {
                    requestFailed = true;
                    AddWarning(execution.Warning, warningsSeen, warnings);
                    failed.Add(index);
                    continue;
                }

                var batch = execution.Result;
                var result = batch.Page;
                answered = true;
                foreach (var warning in result.Warnings)
                {
                    AddWarning(
                        ProviderWarning(invocations[index].Provider.ProviderType, warning),
                        warningsSeen,
                        warnings);
                }

                var preparation = await PrepareCandidatesAsync(
                    invocations[index].Provider,
                    result.Matches,
                    prepared,
                    language,
                    existingLibraryItems,
                    cancellationToken).ConfigureAwait(false);
                foreach (var warning in preparation.Warnings)
                {
                    AddWarning(warning, warningsSeen, warnings);
                }

                var accepted = FilterCandidatesAgainstCriteria(preparation.Candidates, prepared.Criteria);
                var hydrationFailed = preparation.Warnings.Count > 0;
                progress[index] = new(
                    hydrationFailed ? progress[index].Continuation : batch.Continuation,
                    !hydrationFailed && (!result.Supported || !result.PageInfo.HasMore && result.Warnings.Count == 0),
                    [.. progress[index].Pending.Concat(accepted).Where(HasIdentity)]);
                if (result.Warnings.Count > 0 || hydrationFailed)
                {
                    failed.Add(index);
                }
            }

            rows = [.. DeduplicateAcrossProviders([.. progress.Select(item => item.Pending)])
                .SelectMany(group => group)
                .Where(candidate => !CandidateDeliveredIdentities(candidate)
                    .Any(checkpoint.Delivered.Contains))];
            if (rows.Count >= ProviderSearchPageSize ||
                indexes.Length == 0 ||
                progress.All(item => item.Exhausted))
            {
                break;
            }
        }

        if (requestFailed && !answered && rows.Count == 0)
        {
            throw new InvalidOperationException(
                "Metadata search failed; no provider answered successfully. " + string.Join(" ", warnings));
        }

        var page = rows.Take(ProviderSearchPageSize).ToArray();
        var delivered = new HashSet<(string, string, string, string)>(checkpoint.Delivered);
        delivered.UnionWith(page.SelectMany(CandidateDeliveredIdentities));
        var remaining = progress.Select(item => item with
        {
            Pending = [.. item.Pending.Where(candidate =>
                !CandidateDeliveredIdentities(candidate).Any(delivered.Contains))]
        }).ToArray();
        var hasMore = failed.Count > 0 || remaining.Any(item => !item.Exhausted || item.Pending.Count > 0);
        return new ContinuedPage(
            page,
            hasMore,
            rows.Count,
            new SearchCheckpoint(
                checkpoint.Fingerprint,
                checkpoint.RequestIdentity,
                checkpoint.Page + 1,
                delivered,
                remaining));
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
                "Content type {ContentType} has no metadata provider for {Key}={Value}",
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
                    "Metadata provider {ProviderType} failed to fetch a preview for {Key}={Value}",
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
                        "Provider {ProviderType} failed to expand subjects for {Key}={Value}",
                        provider.ProviderType,
                        externalIdKey,
                        externalId);
                }
            }
        }

        if (primary is null)
        {
            logger.LogInformation(
                "No provider resolved a preview for {Key}={Value} with content type {ContentType}",
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
        object? continuation,
        ICache providerHealthCache,
        bool recordHealth,
        CancellationToken cancellationToken)
    {
        using var activity = ContentTelemetry.Source.StartActivity("magpie.metadata.provider_search");
        activity?.SetTag("magpie.provider.type", invocation.Provider.ProviderType);
        activity?.SetTag("magpie.content_type", invocation.ContentType);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsProviderDispatchable(providerHealthCache, logger, invocation.ContentType, invocation.Provider))
            {
                return new ProviderSearchExecution(invocation, null, ProviderSkippedWarning(invocation.Provider));
            }

            var result = await invocation.Provider.ContinueSearchAsync(
                invocation.ContentType,
                invocation.Query,
                maximumResults,
                defaultLanguagePreference,
                continuation,
                cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("magpie.result.supported", result.Page.Supported);
            activity?.SetTag("magpie.result.match_count", result.Page.Matches.Count);
            activity?.SetTag("magpie.result.has_more", result.Page.PageInfo.HasMore);

            if (recordHealth)
            {
                providerHealthCache.Remove(ProviderHealthKey(invocation.ContentType, invocation.Provider));
            }

            return new ProviderSearchExecution(invocation, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MetadataSearchContinuationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "timeout");
            logger.LogWarning(
                exception,
                "An operation timed out while metadata provider {Provider} searched for {ContentType}",
                invocation.Provider.ProviderType,
                invocation.ContentType);
            if (recordHealth)
            {
                RecordProviderFailure(providerHealthCache, invocation.ContentType, invocation.Provider);
            }

            return new ProviderSearchExecution(
                invocation,
                null,
                ProviderSearchTimedOutWarning(invocation.Provider.ProviderType));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("error.type", ex.GetType().FullName);
            logger.LogWarning(
                ex,
                "Metadata provider {Provider} failed to search for {ContentType}",
                invocation.Provider.ProviderType,
                invocation.ContentType);
            if (recordHealth)
            {
                RecordProviderFailure(providerHealthCache, invocation.ContentType, invocation.Provider);
            }

            return new ProviderSearchExecution(
                invocation,
                null,
                ProviderSearchFailedWarning(invocation.Provider.ProviderType));
        }
    }

    private static string ProviderHealthKey(string contentType, IContentHierarchyMetadataSearcher provider) =>
        NormalizedProvider(provider.ProviderType) + "|" + contentType;

    private static void RecordProviderFailure(
        ICache providerHealthCache,
        string contentType,
        IContentHierarchyMetadataSearcher provider)
    {
        var key = ProviderHealthKey(contentType, provider);
        providerHealthCache.TryGet<int>(key, out var failures);
        providerHealthCache.Set(key, failures + 1);
    }

    private static string ProviderSkippedWarning(IContentHierarchyMetadataSearcher provider) =>
        $"{provider.ProviderType}: skipped after repeated failed searches; it is tried again after the cooldown.";

    private static bool IsProviderDispatchable(
        ICache providerHealthCache,
        ILogger<MetadataSearchService> logger,
        string contentType,
        IContentHierarchyMetadataSearcher provider)
    {
        if (!providerHealthCache.TryGet<int>(ProviderHealthKey(contentType, provider), out var failures) ||
            failures < ProviderFailureThreshold)
        {
            return true;
        }

        logger.LogInformation(
            "Metadata provider {Provider} is in cooldown for {ContentType} after {Failures} consecutive " +
            "failed searches",
            provider.ProviderType,
            contentType,
            failures);
        return false;
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

    private static void AddWarning(string? warning, HashSet<string> seen, List<string> warnings)
    {
        if (warning is not null && seen.Add(warning))
        {
            warnings.Add(warning);
            Activity.Current?.AddEvent(new ActivityEvent(
                "warning",
                tags: new ActivityTagsCollection { ["message"] = warning }));
        }
    }

    private static string ProviderSearchFailedWarning(string? providerType) =>
        $"{NormalizedProvider(providerType)} could not finish the search.";

    private static string ProviderSearchTimedOutWarning(string? providerType) =>
        $"{NormalizedProvider(providerType)} did not answer the search in time.";

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

    private static IReadOnlyList<string>? ResolveContentTypes(ContentFilterCriteria criteria)
    {
        IReadOnlyList<string>? resolved = null;
        foreach (var entry in criteria.ContentHierarchyItem.And)
        {
            foreach (var criterion in entry.ContentType.And)
            {
                if (criterion.Negate)
                {
                    continue;
                }

                IReadOnlyList<string> requested = criterion.Operator switch
                {
                    CriteriaValueOperator.Equals when !string.IsNullOrWhiteSpace(criterion.Value) =>
                        [criterion.Value],
                    CriteriaValueOperator.In when criterion.Values is not null =>
                        [.. criterion.Values.Where(value => !string.IsNullOrWhiteSpace(value))],
                    _ => []
                };
                if (requested.Count == 0)
                {
                    continue;
                }

                resolved = resolved is null
                    ? [.. requested.Distinct(StringComparer.OrdinalIgnoreCase)]
                    : [.. resolved.Intersect(requested, StringComparer.OrdinalIgnoreCase)];
            }
        }

        return resolved;
    }

    private static ContentHierarchyItemCriteria ProviderQuery(ContentFilterCriteria criteria, string contentType)
    {
        var item = criteria.ContentHierarchyItem.And.FirstOrDefault() ?? new();
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

    private static (string, string) IdentityKey(MetadataSearchCandidate candidate) =>
        (candidate.ExternalIdSource.ToLowerInvariant(), candidate.ExternalId.ToLowerInvariant());

    private static bool SharesExternalId(MetadataSearchCandidate a, MetadataSearchCandidate b)
    {
        var bKeys = new HashSet<(string, string)>(ExternalIdPairs(b).Select(NormalizedExternalId));
        return ExternalIdPairs(a).Select(NormalizedExternalId).Any(bKeys.Contains);
    }

    private static bool SameTitleAndYear(MetadataSearchCandidate a, MetadataSearchCandidate b)
    {
        if (string.Equals(a.ProviderType, b.ProviderType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var titleA = a.Title.NormalizeContentSearchPhrase();
        var titleB = b.Title.NormalizeContentSearchPhrase();
        if (titleA.Length == 0 || !string.Equals(titleA, titleB, StringComparison.Ordinal))
        {
            return false;
        }

        var year = a.Year ?? a.ReleaseDate?.Year;
        return year is not null && year == (b.Year ?? b.ReleaseDate?.Year);
    }

    private static MetadataSearchCandidate MergeExternalIds(
        MetadataSearchCandidate survivor,
        MetadataSearchCandidate duplicate)
    {
        var seen = new HashSet<(string, string)>(ExternalIdPairs(survivor).Select(NormalizedExternalId));
        var merged = new List<MetadataSearchCandidateExternalId>(survivor.AdditionalExternalIds);
        foreach (var pair in ExternalIdPairs(duplicate))
        {
            if (seen.Add(NormalizedExternalId(pair)))
            {
                merged.Add(new MetadataSearchCandidateExternalId { Source = pair.Source, Value = pair.Value });
            }
        }

        return merged.Count == survivor.AdditionalExternalIds.Count
            ? survivor
            : survivor with { AdditionalExternalIds = merged };
    }

    private static IEnumerable<(string Source, string Value)> ExternalIdPairs(MetadataSearchCandidate candidate)
    {
        yield return (candidate.ExternalIdSource, candidate.ExternalId);
        foreach (var extra in candidate.AdditionalExternalIds)
        {
            yield return (extra.Source, extra.Value);
        }
    }

    private static (string, string) NormalizedExternalId((string Source, string Value) pair) =>
        (pair.Source.NormalizeExternalIdentifierName(), pair.Value.NormalizeExternalIdentifierValue());

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

    private static MatchOutcome MatchesExternalId(MetadataSearchCandidate candidate, ExternalIdCriteria criteria)
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

    private static MatchOutcome VerifyRelationship(MetadataSearchCandidate candidate, RelationshipCriteria relationship)
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

    private static bool CandidateNeedsHydration(MetadataSearchCandidate candidate, MetadataCriteria criteria) =>
        criteria.Title.And.Any(criterion =>
            MatchesString(candidate.Title, criterion) == MatchOutcome.Unverifiable) ||
        criteria.Language.And.Any(criterion =>
            MatchesString(candidate.Language, criterion) == MatchOutcome.Unverifiable) ||
        criteria.Overview.And.Any(criterion =>
            MatchesString(candidate.Overview, criterion) == MatchOutcome.Unverifiable) ||
        criteria.PublishDate.And.Any(criterion =>
            MatchesPublishDate(candidate, criterion) == MatchOutcome.Unverifiable) ||
        criteria.ExternalIds.Some.Any(group =>
            VerifyExternalIdGroup(candidate, group) == MatchOutcome.Unverifiable) ||
        criteria.ExternalIds.None.Any(excluded =>
            VerifyExternalIdExclusion(candidate, excluded) == MatchOutcome.Unverifiable) ||
        criteria.Relationships.Some.Any(group =>
            VerifyRelationshipGroup(candidate, group) == MatchOutcome.Unverifiable) ||
        criteria.Relationships.None.Any(excluded =>
            VerifyRelationshipExclusion(candidate, excluded) == MatchOutcome.Unverifiable);

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
            ReleaseDatePrecision = primary.PublishDatePrecision,
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

    private IReadOnlyList<IReadOnlyList<MetadataSearchCandidate>> DeduplicateAcrossProviders(
        IReadOnlyList<IReadOnlyList<MetadataSearchCandidate>> groups)
    {
        var deduped = new List<List<MetadataSearchCandidate>>(groups.Count);
        var survivorGroupIndex = new List<int>();
        var survivorItemIndex = new List<int>();
        foreach (var group in groups)
        {
            var kept = new List<MetadataSearchCandidate>(group.Count);
            deduped.Add(kept);
            var groupIndex = deduped.Count - 1;
            foreach (var candidate in group)
            {
                var survivorIndex = -1;
                for (var i = 0; i < survivorGroupIndex.Count; i++)
                {
                    if (IsSameItem(deduped[survivorGroupIndex[i]][survivorItemIndex[i]], candidate))
                    {
                        survivorIndex = i;
                        break;
                    }
                }

                if (survivorIndex < 0)
                {
                    kept.Add(candidate);
                    survivorGroupIndex.Add(groupIndex);
                    survivorItemIndex.Add(kept.Count - 1);
                    continue;
                }

                var survivorGroup = survivorGroupIndex[survivorIndex];
                var survivorItem = survivorItemIndex[survivorIndex];
                deduped[survivorGroup][survivorItem] =
                    MergeExternalIds(deduped[survivorGroup][survivorItem], candidate);
            }
        }

        return deduped;
    }

    private bool IsSameItem(MetadataSearchCandidate a, MetadataSearchCandidate b)
    {
        var aIdentity = CandidateLibraryIdentity(a);
        var bIdentity = CandidateLibraryIdentity(b);
        if (aIdentity.Item1 != bIdentity.Item1 || aIdentity.Item2 != bIdentity.Item2)
        {
            return false;
        }

        return SharesExternalId(a, b) || SameTitleAndYear(a, b);
    }

    private async Task<(IReadOnlyList<MetadataSearchCandidate> Candidates, IReadOnlyList<string> Warnings)>
        PrepareCandidatesAsync(
        IContentHierarchyMetadataAdvancedSearcher provider,
        IReadOnlyList<MetadataSearchCandidate> candidates,
        PreparedSearch prepared,
        string defaultLanguagePreference,
        Dictionary<(string, string, string, string), Guid> existingLibraryItems,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return ([], []);
        }

        var existingCandidates = await FindExistingCandidatesAsync(candidates, cancellationToken)
            .ConfigureAwait(false);
        foreach (var existingCandidate in existingCandidates)
        {
            existingLibraryItems.TryAdd(existingCandidate.Key, existingCandidate.Value);
        }

        var hydrated = new MetadataSearchCandidate?[candidates.Count];
        var warnings = new string?[candidates.Count];
        await Task.WhenAll(
            Enumerable.Range(0, candidates.Count).Select(
            async index =>
            {
                var candidate = candidates[index];
                var candidateKey = CandidateLibraryIdentity(candidate);
                if (existingCandidates.ContainsKey(candidateKey))
                {
                    hydrated[index] = candidate;
                    return;
                }

                if (FilterCandidatesAgainstCriteria([candidate], prepared.Criteria, preHydrationOnly: true).Count == 0)
                {
                    return;
                }

                if (!CandidateNeedsHydration(candidate, prepared.Criteria))
                {
                    hydrated[index] = candidate;
                    return;
                }

                var cacheKey = new
                {
                    Service = nameof(MetadataSearchService),
                    Kind = "hydrate",
                    ProviderType = provider.ProviderType,
                    candidate.ContentType,
                    Level = candidateKey.Item2,
                    Source = candidate.ExternalIdSource,
                    candidate.ExternalId,
                    Language = defaultLanguagePreference
                };

                using var activity = ContentTelemetry.Source.StartActivity("magpie.metadata.hydrate");
                activity?.SetTag("magpie.provider.type", provider.ProviderType);
                activity?.SetTag("magpie.content_type", candidate.ContentType);

                MetadataSearchCandidate? hydratedCandidate;
                try
                {
                    hydratedCandidate = await _hydrationCache.GetOrAddAsync(
                        JsonSerializer.Serialize(cacheKey),
                        async token =>
                        {
                            return await provider
                                .HydrateAsync(candidate, defaultLanguagePreference, token)
                                .WaitAsync(token)
                                .ConfigureAwait(false);
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
                        "Metadata provider {Provider} failed to hydrate {Source}={Value}",
                        provider.ProviderType,
                        candidate.ExternalIdSource,
                        candidate.ExternalId);
                    warnings[index] =
                        $"{NormalizedProvider(provider.ProviderType)} could not hydrate a search result. " +
                        "The search is incomplete.";
                    return;
                }

                hydrated[index] = hydratedCandidate is null
                    ? candidate
                    : hydratedCandidate with
                    {
                        SubjectReferences = candidate.SubjectReferences is { Count: > 0 }
                            ? candidate.SubjectReferences
                            : hydratedCandidate.SubjectReferences
                    };
            })).ConfigureAwait(false);

        return ([.. hydrated.OfType<MetadataSearchCandidate>()], [.. warnings.OfType<string>().Distinct()]);
    }

    private IEnumerable<(string, string, string, string)> CandidateDeliveredIdentities(
        MetadataSearchCandidate candidate)
    {
        var identity = CandidateLibraryIdentity(candidate);
        return ExternalIdPairs(candidate).Select(NormalizedExternalId)
            .Select(pair => (identity.Item1, identity.Item2, pair.Item1, pair.Item2));
    }

    private (string, string, string, string) CandidateLibraryIdentity(MetadataSearchCandidate candidate)
    {
        var level = candidate.Level;
        if (string.IsNullOrWhiteSpace(level))
        {
            var roots = contentTypeRegistry.GetRootLevels(candidate.ContentType);
            level = roots.Count switch
            {
                0 => candidate.ContentType,
                1 => roots[0].Slug,
                _ => string.Empty
            };
        }

        return (
            candidate.ContentType.ToLowerInvariant(),
            level.ToLowerInvariant(),
            candidate.ExternalIdSource.ToLowerInvariant(),
            candidate.ExternalId.ToLowerInvariant());
    }

    private async Task<IReadOnlyDictionary<(string, string, string, string), Guid>> FindExistingCandidatesAsync(
        IReadOnlyList<MetadataSearchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return new Dictionary<(string, string, string, string), Guid>();
        }

        var identities = candidates
            .Select(CandidateLibraryIdentity)
            .Where(identity => !string.IsNullOrWhiteSpace(identity.Item2) &&
                !string.IsNullOrWhiteSpace(identity.Item3) && !string.IsNullOrWhiteSpace(identity.Item4))
            .Distinct()
            .ToArray();
        if (identities.Length == 0)
        {
            return new Dictionary<(string, string, string, string), Guid>();
        }

        return await contentHierarchyItemRepository.FindByExternalIdentitiesAsync(identities, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record SearchCheckpoint(
        string Fingerprint,
        string RequestIdentity,
        int Page,
        HashSet<(string, string, string, string)> Delivered,
        IReadOnlyList<ProviderProgress> Providers);

    private sealed record ProviderProgress(
        object? Continuation,
        bool Exhausted,
        IReadOnlyList<MetadataSearchCandidate> Pending);

    private sealed record ContinuedPage(
        IReadOnlyList<MetadataSearchCandidate> Rows,
        bool HasMore,
        int MergedCount,
        SearchCheckpoint Next);

    private sealed record PreparedSearch(MetadataCriteria Criteria, string? TitleQuery);

    private sealed record ProviderSearchInvocation(
        string ContentType,
        IContentHierarchyMetadataAdvancedSearcher Provider,
        ContentHierarchyItemCriteria Query);

    private sealed record ProviderSearchExecution(
        ProviderSearchInvocation Invocation,
        MetadataSearchBatch? Result,
        string? Warning);
}
