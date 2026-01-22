using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Content;
using Magpie.Models.Metadata;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Searches for content hierarchy items in an external metadata source using advanced criteria.
/// </summary>
public interface IContentHierarchyMetadataAdvancedSearcher
{
    /// <summary>
    /// Gets the content type slugs supported by the searcher.
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>
    /// Searches for content hierarchy items by advanced criteria.
    /// </summary>
    /// <param name="levelDefinition">The content hierarchy level definition to search for.</param>
    /// <param name="criteria">The criteria to search by, mapped to metadata fields.</param>
    /// <param name="customFieldCriteria">The custom field criteria to search by.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MetadataSearchResult>>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        IReadOnlyDictionary<MetadataField, string> criteria,
        IReadOnlyDictionary<string, string> customFieldCriteria,
        string defaultLanguagePreference,
        CancellationToken cancellationToken);
}
