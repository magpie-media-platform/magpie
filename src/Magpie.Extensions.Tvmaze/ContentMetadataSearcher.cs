using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Extensions.Tvmaze.Pipeline;
using Magpie.Models;
using Magpie.Models.Data;
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

    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes => [TvContentType];

    /// <inheritdoc />
    public async Task<MetadataSearchBatch> ContinueSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        object? continuation,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = JsonSerializer.Serialize(new { contentType, query, defaultLanguagePreference });
        if (continuation is not null &&
            (continuation is not SearchCheckpoint previous || previous.Fingerprint != fingerprint))
        {
            throw new MetadataSearchContinuationException("The TVmaze checkpoint does not match this search.");
        }

        if (contentType != TvContentType)
        {
            return new MetadataSearchBatch();
        }

        var progress = (continuation as SearchCheckpoint)?.Progress.Copy() ?? new QueryProgress();
        progress.ValidateOnly = maxResults == 0;
        var plan = planner.Plan(query, maxResults) with { Progress = progress };
        var page = await executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
        progress.Delivered.UnionWith(page.Matches.Select(candidate =>
            int.Parse(candidate.ExternalId, CultureInfo.InvariantCulture)));
        cancellationToken.ThrowIfCancellationRequested();
        return new MetadataSearchBatch { Page = page, Continuation = new SearchCheckpoint(fingerprint, progress) };
    }

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
            logger.LogDebug("TVmaze does not support level slug {Slug}", levelDefinition.Slug);
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
                "TVmaze does not support advanced search for content type {ContentType}",
                contentType);
            return MetadataSearchPage.NotSupported;
        }

        var plan = planner.Plan(query, maxResults);
        return await executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
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

        var show = await client.GetShowWithEmbedsAsync(id, ["cast", "crew"], cancellationToken).ConfigureAwait(false);
        return show.ToMetadataSearchCandidate();
    }

    private sealed record SearchCheckpoint(string Fingerprint, QueryProgress Progress);
}
