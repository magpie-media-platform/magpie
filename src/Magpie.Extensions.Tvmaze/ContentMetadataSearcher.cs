using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Extensions.Tvmaze.Pipeline;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tvmaze;

internal sealed class ContentMetadataSearcher(
    IClient client,
    ISearchPlanner planner,
    ISearchExecutor executor,
    ILogger<ContentMetadataSearcher> logger) : IContentHierarchyMetadataAdvancedSearcher
{
    private const string TvContentType = "tv";
    private const string ShowLevelSlug = "show";
    private const int DefaultPageSize = 25;

    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes => [TvContentType];

    public async Task<IEnumerable<MetadataSearchCandidate>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return [];
        }

        if (page > 1)
        {
            return [];
        }

        if (levelDefinition.Slug != ShowLevelSlug)
        {
            logger.LogDebug("The TVmaze searcher does not support level slug {Slug}.", levelDefinition.Slug);
            return [];
        }

        var hits = await client.SearchShowsAsync(searchTerm.Trim(), cancellationToken).ConfigureAwait(false);

        var results = new List<MetadataSearchCandidate>(hits.Count);
        foreach (var hit in hits)
        {
            var result = (hit?.Show).ToMetadataSearchCandidate();
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    public async Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (contentType != TvContentType)
        {
            logger.LogDebug(
                "The TVmaze searcher does not support advanced search for content type {ContentType}.",
                contentType);
            return MetadataSearchPage.NotSupported;
        }

        var plan = planner.Plan(query, maxResults);
        return await executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    internal Task<MetadataSearchPage> AdvancedSearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        MetadataCriteria criteria,
        int page,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (levelDefinition.Slug != ShowLevelSlug)
        {
            logger.LogDebug(
                "The TVmaze searcher does not support advanced search for level slug {Slug}.",
                levelDefinition.Slug);
            return Task.FromResult(MetadataSearchPage.NotSupported);
        }

        return AdvancedSearchAsync(
            TvContentType,
            new ContentHierarchyItemCriteria
            {
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = levelDefinition.Slug }]
                },
                Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
            },
            (int)Math.Min((long)Math.Max(1, page) * DefaultPageSize, int.MaxValue - 1L),
            defaultLanguagePreference,
            cancellationToken);
    }

    public async Task<MetadataSearchCandidate?> HydrateAsync(
        MetadataSearchCandidate candidate,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (candidate.ProviderType != ProviderType ||
            candidate.ContentType != TvContentType ||
            candidate.ExternalIdSource != Constants.ProviderType ||
            !int.TryParse(candidate.ExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
            id <= 0)
        {
            return null;
        }

        var show = await client.GetShowWithCreditsAsync(id, cancellationToken).ConfigureAwait(false);
        return show.ToMetadataSearchCandidate();
    }
}
