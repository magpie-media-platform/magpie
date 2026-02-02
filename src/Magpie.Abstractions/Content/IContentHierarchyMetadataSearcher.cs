using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides external metadata search capabilities.
/// </summary>
public interface IContentHierarchyMetadataSearcher
{
    /// <summary>
    /// The unique type identifier for the searcher.
    /// </summary>
    public string ProviderType { get; }

    /// <summary>
    /// Get the content type slugs supported by the searcher.
    /// </summary>
    IEnumerable<string> SupportedContentTypes { get; }

    /// <summary>
    /// Search for content hierarchy items by search term.
    /// </summary>
    /// <param name="levelDefinition">The content hierarchy level definition to search.</param>
    /// <param name="searchTerm">The text search term.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<MetadataSearchResultItem>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
