using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Wikidata;

internal sealed class ContentMetadataSearcher(
    ISettingsResolver settingsResolver,
    ISearchEngine queryEngine,
    IQueryCoordinator cache,
    TimeProvider timeProvider,
    ILogger<ContentMetadataSearcher> logger) : IContentHierarchyMetadataAdvancedSearcher
{
    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes =>
        [Constants.MovieContentType, Constants.TvContentType];

    public async Task<IEnumerable<MetadataSearchCandidate>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm) || !TrySearchKind(levelDefinition.Slug, out var kind))
        {
            return [];
        }

        var context = await settingsResolver.ResolveAsync(defaultLanguagePreference, cancellationToken)
            .ConfigureAwait(false);
        var result = await queryEngine.SearchAsync(
            new QueryRequest
            {
                EntityKind = kind,
                Criteria = new MetadataCriteria(),
                Context = context,
                Page = page,
                OrdinaryTerm = searchTerm.Trim()
            },
            cancellationToken).ConfigureAwait(false);
        return [.. result.Hits.Select(hit => hit.ToMetadataSearchCandidate(kind, levelDefinition.Slug))];
    }

    public async Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        var level = contentType == Constants.MovieContentType
            ? Constants.MovieLevel
            : contentType == Constants.TvContentType
                ? Constants.ShowLevel
                : string.Empty;
        if (!TrySearchKind(level, out var kind))
        {
            logger.LogDebug(
                "Wikidata advanced search does not support content type {ContentType}",
                contentType);
            return MetadataSearchPage.NotSupported;
        }

        var contentTypeCriteria = query.ContentType.And.ToArray();
        var levelCriteria = query.Level.And.ToArray();
        var contentTypeScope = EvaluateScope(contentTypeCriteria, contentType);
        var levelScope = EvaluateScope(levelCriteria, level);
        if (contentTypeScope == CriteriaMatchResult.Unverifiable ||
            levelScope == CriteriaMatchResult.Unverifiable)
        {
            return EmptySupported();
        }

        if (contentTypeScope == CriteriaMatchResult.Rejected ||
            levelScope == CriteriaMatchResult.Rejected)
        {
            return EmptySupported();
        }

        var context = await settingsResolver.ResolveAsync(defaultLanguagePreference, cancellationToken)
            .ConfigureAwait(false);
        var result = await queryEngine.SearchAsync(
            new QueryRequest
            {
                EntityKind = kind,
                Criteria = query.MergeMetadata(),
                Context = context,
                Page = 1,
                MaximumResults = Math.Max(1, maxResults)
            },
            cancellationToken).ConfigureAwait(false);
        if (!result.Supported)
        {
            return new MetadataSearchPage
            {
                Supported = false,
                Matches = [],
                PageInfo = ProviderSearchPageInfo.Unsupported,
                Warnings = [.. result.Warnings]
            };
        }

        return new MetadataSearchPage
        {
            Supported = true,
            Matches = [.. result.Hits.Select(hit => hit.ToMetadataSearchCandidate(kind, level))],
            PageInfo = result.PageInfo,
            Warnings = [.. result.Warnings]
        };
    }

    public async Task<MetadataSearchCandidate?> HydrateAsync(
        MetadataSearchCandidate candidate,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (candidate.ProviderType != ProviderType ||
            !TrySearchKind(candidate.Level, out var kind) ||
            !candidate.ExternalId.TryNormalizeQid(out var qid))
        {
            return null;
        }

        var context = await settingsResolver.ResolveAsync(defaultLanguagePreference, cancellationToken)
            .ConfigureAwait(false);
        var entity = await cache.GetEntityAsync(qid!, context, cancellationToken).ConfigureAwait(false);
        if (entity is null || !entity.MatchesKind(kind))
        {
            return null;
        }

        var supportQids = entity.RequiredSupportQids();
        var support = await cache.GetEntitiesAsync(supportQids, context, cancellationToken)
            .ConfigureAwait(false);
        var contribution = entity.ToMetadataContribution(
            kind,
            context.Language,
            support,
            timeProvider.GetUtcNow().UtcDateTime);
        return contribution?.ToMetadataSearchCandidate(candidate, kind);
    }

    private static bool TrySearchKind(string? level, out EntityKind kind)
    {
        var resolved = level switch
        {
            Constants.MovieLevel => EntityKind.Movie,
            Constants.ShowLevel => EntityKind.Show,
            Constants.SeasonLevel => EntityKind.Season,
            Constants.EpisodeLevel => EntityKind.Episode,
            _ => (EntityKind?)null
        };
        kind = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }

    private static MetadataSearchPage EmptySupported()
    {
        return new()
        {
            Supported = true,
            Matches = [],
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = 1,
                HasMore = false
            },
            Warnings = []
        };
    }

    private static string[] TextValues(PropertyCriterion<string> criterion)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(criterion.Value))
        {
            values.Add(criterion.Value.Trim());
        }

        values.AddRange((criterion.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
        return [.. values.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static CriteriaMatchResult EvaluateScope(
        IReadOnlyList<PropertyCriterion<string>> criteria,
        string value)
    {
        var result = CriteriaMatchResult.Matched;
        foreach (var criterion in criteria)
        {
            var criterionResult = CriteriaMatcher.Match(
                value,
                criterion.Operator,
                TextValues(criterion),
                criterion.Negate);
            if (criterionResult == CriteriaMatchResult.Rejected)
            {
                result = CriteriaMatchResult.Rejected;
            }

            if (criterionResult == CriteriaMatchResult.Unverifiable)
            {
                return CriteriaMatchResult.Unverifiable;
            }
        }

        return result;
    }
}
