using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.MetadataSearch;

namespace Magpie.Sdk.Services;

/// <summary>
/// Searches content metadata providers.
/// </summary>
public interface IMetadataSearchService
{
    /// <summary>
    /// Searches providers with parsed filter criteria.
    /// </summary>
    /// <param name="criteria">The filter criteria</param>
    /// <param name="contentType">The content type, or <see langword="null"/> to use the criteria</param>
    /// <param name="page">The one-based page number</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ProviderMetadataCandidateSearchResult> SearchCandidatesAsync(
        ContentFilterCriteria criteria,
        string? contentType,
        int page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches providers with text.
    /// </summary>
    /// <param name="query">The search text</param>
    /// <param name="contentType">The content type</param>
    /// <param name="page">The one-based page number</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MetadataSearchPage> SearchAsync(
        string query,
        string contentType,
        int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A provider preview for an external id.
    /// </summary>
    /// <param name="providerType">The provider type</param>
    /// <param name="externalIdKey">The external id source</param>
    /// <param name="externalId">The external id</param>
    /// <param name="contentType">The content type</param>
    /// <param name="levelSlug">The level slug, or <see langword="null"/> for the default</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentPreview?> PreviewByExternalIdAsync(
        string providerType,
        string externalIdKey,
        string externalId,
        string contentType,
        string? levelSlug,
        CancellationToken cancellationToken = default);
}
