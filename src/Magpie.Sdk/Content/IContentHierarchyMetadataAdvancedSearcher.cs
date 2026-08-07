using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.MetadataSearch;

namespace Magpie.Sdk.Content;

/// <summary>
/// A metadata source that accepts structured search criteria.
/// </summary>
public interface IContentHierarchyMetadataAdvancedSearcher : IContentHierarchyMetadataSearcher
{
    /// <summary>
    /// Searches for content items with structured filter criteria.
    /// </summary>
    /// <param name="contentType">The slug of the content type to search</param>
    /// <param name="query">The parsed MQL criteria for the content hierarchy item</param>
    /// <param name="maxResults">The maximum number of verified results to return</param>
    /// <param name="defaultLanguagePreference">The language to prefer for result text</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hydrates a candidate from an advanced search.
    /// </summary>
    /// <param name="candidate">The search candidate to hydrate</param>
    /// <param name="defaultLanguagePreference">The language to prefer for hydrated metadata</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MetadataSearchCandidate?> HydrateAsync(
        MetadataSearchCandidate candidate,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
