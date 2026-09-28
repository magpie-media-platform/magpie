using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class ContentMetadataSearcher(
    IEnumerable<IContentTypeDefinitionProvider> contentTypeDefinitionProviders,
    ISearchEngine queryEngine,
    ILogger<ContentMetadataSearcher> logger)
    : IContentHierarchyMetadataAdvancedSearcher
{
    private readonly IReadOnlyList<BookHierarchy> _hierarchies = contentTypeDefinitionProviders.ToBookHierarchies();

    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes =>
        _hierarchies.Select(hierarchy => hierarchy.ContentTypeSlug).Distinct(StringComparer.Ordinal);

    public async Task<IEnumerable<MetadataSearchCandidate>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var hierarchy = SupportedHierarchy(levelDefinition);
        if (hierarchy is null || string.IsNullOrWhiteSpace(searchTerm))
        {
            return [];
        }

        try
        {
            var result = await queryEngine.SearchAsync(
                new QueryRequest
                {
                    Intent = QueryIntent.OrdinarySearch,
                    Hierarchy = hierarchy,
                    Value = searchTerm,
                    LanguagePreference = defaultLanguagePreference,
                    Page = page
                },
                cancellationToken).ConfigureAwait(false);
            return [.. result.Hits.Select(hit => hit.ToMetadataSearchCandidate(hierarchy, defaultLanguagePreference))];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Open Library ordinary search failed");
            return [];
        }
    }

    public Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
        => SearchPageAsync(contentType, query, maxResults, defaultLanguagePreference, null, cancellationToken);

    /// <inheritdoc />
    public async Task<MetadataSearchBatch> ContinueSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        object? continuation,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = JsonSerializer.Serialize(new { contentType, query, defaultLanguagePreference, _hierarchies });
        if (continuation is not null &&
            (continuation is not SearchCheckpoint previous || previous.Fingerprint != fingerprint))
        {
            throw new MetadataSearchContinuationException("The Open Library checkpoint does not match this search.");
        }

        var progress = (continuation as SearchCheckpoint)?.Progress.Copy() ?? new QueryProgress();
        progress.ValidateOnly = maxResults == 0;
        var page = await SearchPageAsync(contentType, query, maxResults, defaultLanguagePreference,
            progress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new MetadataSearchBatch
        {
            Page = page,
            Continuation = new SearchCheckpoint(fingerprint, progress)
        };
    }

    private async Task<MetadataSearchPage> SearchPageAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        QueryProgress? progress,
        CancellationToken cancellationToken)
    {
        var hierarchy = SupportedHierarchy(contentType, query);
        if (hierarchy is null)
        {
            return MetadataSearchPage.NotSupported;
        }

        var result = await queryEngine.SearchAsync(
            new QueryRequest
            {
                Intent = QueryIntent.AdvancedSearch,
                Progress = progress,
                Hierarchy = hierarchy,
                Criteria = query,
                LanguagePreference = defaultLanguagePreference,
                MaxResults = maxResults
            },
            cancellationToken).ConfigureAwait(false);
        var metadata = query.MergeMetadata();
        var projectAnyLanguage = RequiresLanguagePresenceProjection(metadata);
        return new MetadataSearchPage
        {
            Supported = true,
            Matches = [.. result.Hits
                .Select(hit => hit.ToMetadataSearchCandidate(
                    hierarchy,
                    result.CandidateLanguagePreferences.FirstOrDefault(preference => hit.Languages.Any(language =>
                        string.Equals(
                            language.NormalizeLanguage(),
                            preference,
                            StringComparison.OrdinalIgnoreCase))) ??
                    string.Empty,
                    projectAnyLanguage))],
            PageInfo = result.PageInfo,
            Warnings = result.Warnings
        };
    }

    public async Task<MetadataSearchCandidate?> HydrateAsync(
        MetadataSearchCandidate candidate,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        var hierarchy = _hierarchies.FindBookHierarchy(candidate.ContentType, candidate.Level);
        if (hierarchy is null ||
            candidate.ProviderType != ProviderType ||
            !string.Equals(
                candidate.ExternalIdSource,
                Constants.ProviderType,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolution = await queryEngine.ResolveWorkAsync(
            new QueryRequest
            {
                Intent = QueryIntent.WorkResolution,
                Hierarchy = hierarchy,
                Source = Constants.ProviderType,
                Value = candidate.ExternalId,
                LanguagePreference = defaultLanguagePreference
            },
            cancellationToken).ConfigureAwait(false);
        var languagePreference = string.IsNullOrWhiteSpace(defaultLanguagePreference)
            ? candidate.Language ?? string.Empty
            : defaultLanguagePreference;
        if (resolution.Hit is null)
        {
            return null;
        }

        var hydrated = resolution.Hit.ToMetadataSearchCandidate(hierarchy, languagePreference);
        return hydrated with
        {
            Language = hydrated.Language ?? candidate.Language
        };
    }

    private bool RequiresLanguagePresenceProjection(MetadataCriteria criteria)
    {
        return criteria.Language.And.Any(criterion =>
            criterion.Operator == CriteriaValueOperator.IsNotNull && !criterion.Negate ||
            criterion.Operator == CriteriaValueOperator.IsNull && criterion.Negate);
    }

    private BookHierarchy? SupportedHierarchy(ContentHierarchyLevelDefinition levelDefinition)
    {
        var hierarchy = _hierarchies.FindBookHierarchy(levelDefinition.Slug);
        if (hierarchy is null)
        {
            logger.LogDebug("Open Library searcher did not find a compatible book hierarchy");
            return null;
        }

        return hierarchy;
    }

    private BookHierarchy? SupportedHierarchy(string contentType, ContentHierarchyItemCriteria query)
    {
        var requestedLevels = query.Level.And
            .Where(criterion => !criterion.Negate)
            .SelectMany(criterion => criterion.Operator switch
            {
                CriteriaValueOperator.Equals when criterion.Value is not null => [criterion.Value],
                CriteriaValueOperator.In => criterion.Values ?? [],
                _ => []
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requestedLevels.Count == 0)
        {
            return _hierarchies.FirstOrDefault(hierarchy =>
                string.Equals(hierarchy.ContentTypeSlug, contentType, StringComparison.Ordinal));
        }

        var matches = _hierarchies
            .Where(hierarchy =>
                string.Equals(hierarchy.ContentTypeSlug, contentType, StringComparison.Ordinal) &&
                requestedLevels.Contains(hierarchy.BookLevelSlug, StringComparer.Ordinal))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private sealed record SearchCheckpoint(string Fingerprint, QueryProgress Progress);
}
