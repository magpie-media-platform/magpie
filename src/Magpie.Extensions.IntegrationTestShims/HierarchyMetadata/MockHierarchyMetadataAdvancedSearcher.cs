using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.IntegrationTestShims.HierarchyMetadata;

internal sealed class MockHierarchyMetadataAdvancedSearcher : IContentHierarchyMetadataAdvancedSearcher
{
    private readonly AuditWriter _audit = new(MockHierarchyMetadataProviderType.Primary);
    private readonly LatencyInjector _latency = new(MockHierarchyMetadataProviderType.Primary);
    private readonly ScenarioReader _scenarios = new(MockHierarchyMetadataProviderType.Primary);

    public string ProviderType => MockHierarchyMetadataProviderType.Primary;

    public IEnumerable<string> SupportedContentTypes => ["*"];

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
            throw new MetadataSearchContinuationException("The fixture checkpoint does not match this search.");
        }

        var offset = (continuation as SearchCheckpoint)?.Offset ?? 0;
        if (maxResults == 0)
        {
            return new MetadataSearchBatch { Continuation = continuation };
        }

        var result = await AdvancedSearchAsync(contentType, query, maxResults, defaultLanguagePreference,
            cancellationToken).ConfigureAwait(false);
        return new MetadataSearchBatch
        {
            Page = result with
            {
                Matches = [.. result.Matches.Skip(offset).Take(maxResults)],
                PageInfo = new ProviderSearchPageInfo
                {
                    RequestedPage = 1,
                    HasMore = result.Matches.Count > offset + maxResults
                }
            },
            Continuation = new SearchCheckpoint(fingerprint, offset + maxResults)
        };
    }

    public async Task<IEnumerable<MetadataSearchCandidate>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        const string method = "searchAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarios
            .ReadAsync<List<MetadataSearchCandidate>>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _audit
            .WriteAsync(
                method,
                new { levelDefinition, searchTerm, defaultLanguagePreference, page },
                response,
                cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    public async Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        const string method = "advancedSearchAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        List<MetadataSearchCandidate> candidates;
        try
        {
            candidates = await _scenarios
                .ReadAsync<List<MetadataSearchCandidate>>(method, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ScenarioFileMissingException)
        {
            return MetadataSearchPage.NotSupported with { Supported = true };
        }

        await _audit
            .WriteAsync(
                method,
                new { contentType, query, maxResults, defaultLanguagePreference },
                candidates,
                cancellationToken)
            .ConfigureAwait(false);

        return new MetadataSearchPage
        {
            Supported = true,
            Matches = candidates,
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = 1,
                HasMore = false
            }
        };
    }

    public async Task<MetadataSearchCandidate?> HydrateAsync(
        MetadataSearchCandidate candidate,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        const string method = "hydrateAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        MetadataSearchCandidate? response;
        try
        {
            response = await _scenarios
                .ReadAsync<MetadataSearchCandidate?>(method, candidate.ExternalId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ScenarioFileMissingException)
        {
            response = candidate;
        }

        await _audit
            .WriteAsync(
                method,
                new { candidate, defaultLanguagePreference },
                response,
                cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    private sealed record SearchCheckpoint(string Fingerprint, int Offset);
}
