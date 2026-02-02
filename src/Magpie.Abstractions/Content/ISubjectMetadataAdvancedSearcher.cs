using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Enums;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides external subject metadata search capabilities using advanced search criteria.
/// </summary>
public interface ISubjectMetadataAdvancedSearcher : ISubjectMetadataSearcher
{
    /// <summary>
    /// Perform an advanced search for subjects.
    /// </summary>
    /// <param name="subjectType">The subject type to search.</param>
    /// <param name="criteria">The criteria to search by, mapped to metadata fields.</param>
    /// <param name="customFieldCriteria">The custom field criteria to search by.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<MetadataSearchResultItem>> AdvancedSearchAsync(
        string subjectType,
        IReadOnlyDictionary<MetadataField, string> criteria,
        IReadOnlyDictionary<string, string> customFieldCriteria,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
