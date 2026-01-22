using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Metadata;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Searches for subjects in an external metadata source using advanced criteria.
/// </summary>
public interface ISubjectMetadataAdvancedSearcher
{
    /// <summary>
    /// Gets the subject kinds supported by the searcher.
    /// </summary>
    IReadOnlyList<string> SupportedSubjectKinds { get; }

    /// <summary>
    /// Searches for subjects by advanced criteria.
    /// </summary>
    /// <param name="subjectKind">The subject kind to search within.</param>
    /// <param name="criteria">The criteria to search by, mapped to metadata fields.</param>
    /// <param name="customFieldCriteria">The custom field criteria to search by.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MetadataSearchResult>>> SearchAsync(
        string subjectKind,
        IReadOnlyDictionary<MetadataField, string> criteria,
        IReadOnlyDictionary<string, string> customFieldCriteria,
        string defaultLanguagePreference,
        CancellationToken cancellationToken);
}
