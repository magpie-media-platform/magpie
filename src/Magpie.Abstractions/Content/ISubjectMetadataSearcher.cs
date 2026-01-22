using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Metadata;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Searches for subjects in an external metadata source.
/// </summary>
public interface ISubjectMetadataSearcher
{
    /// <summary>
    /// Gets the subject kinds supported by the searcher.
    /// </summary>
    IReadOnlyList<string> SupportedSubjectKinds { get; }

    /// <summary>
    /// Searches for subjects by search term.
    /// </summary>
    /// <param name="subjectKind">The subject kind to search within.</param>
    /// <param name="searchTerm">The search term text.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MetadataSearchResult>>> SearchAsync(
        string subjectKind,
        string searchTerm,
        string defaultLanguagePreference,
        CancellationToken cancellationToken);
}
