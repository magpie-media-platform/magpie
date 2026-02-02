using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Search for subjects in an external metadata source.
/// </summary>
public interface ISubjectMetadataSearcher
{
    /// <summary>
    /// The unique type identifier for the searcher.
    /// </summary>
    public string ProviderType { get; }

    /// <summary>
    /// The subject kinds supported by the searcher.
    /// </summary>
    IEnumerable<string> SupportedSubjectKinds { get; }

    /// <summary>
    /// Search for subjects by search term.
    /// </summary>
    /// <param name="subjectKind">The subject kind to search.</param>
    /// <param name="searchTerm">The search term text.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for search results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<MetadataSearchResultItem>> SearchAsync(
        string subjectKind,
        string searchTerm,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
