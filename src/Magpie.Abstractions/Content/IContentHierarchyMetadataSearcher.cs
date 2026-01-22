using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Content;
using Magpie.Models.Metadata;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Searches for content hierarchy items in an external metadata source.
/// </summary>
public interface IContentHierarchyMetadataSearcher
{
    /// <summary>
    /// Gets the content type slugs supported by the searcher.
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>
    /// Searches for content hierarchy items by search term.
    /// </summary>
    /// <param name="levelDefinition">The content hierarchy level definition to search for.</param>
    /// <param name="searchTerm">The search term text.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MetadataSearchResult>>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        CancellationToken cancellationToken);
}
