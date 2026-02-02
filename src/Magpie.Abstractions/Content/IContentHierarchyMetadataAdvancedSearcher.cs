using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides external metadata search capabilities using advanced search criteria..
/// </summary>
public interface IContentHierarchyMetadataAdvancedSearcher : IContentHierarchyMetadataSearcher
{
    /// <summary>
    /// Perform an advanced search for content items.
    /// </summary>
    /// <param name="levelDefinition">The content hierarchy level definition to search for.</param>
    /// <param name="criteria">The criteria to search.</param>
    /// <param name="customFieldCriteria">The custom field criteria to search.</param>
    /// <param name="defaultLanguagePreference">The default language to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<MetadataSearchResultItem>> AdvancedSearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        IReadOnlyDictionary<MetadataField, PropertyCriterion<string>> criteria,
        IReadOnlyDictionary<string, PropertyCriterion<string>> customFieldCriteria,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
