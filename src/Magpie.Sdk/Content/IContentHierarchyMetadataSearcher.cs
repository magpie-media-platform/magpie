using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.MetadataSearch;

namespace Magpie.Sdk.Content;

/// <summary>
/// A metadata source that searches for content hierarchy items.
/// </summary>
public interface IContentHierarchyMetadataSearcher : IContentHierarchyMetadataSource
{
    /// <summary>
    /// Searches for content hierarchy items by text term.
    /// </summary>
    /// <param name="levelDefinition">The content hierarchy level definition to search</param>
    /// <param name="searchTerm">The text to search for</param>
    /// <param name="defaultLanguagePreference">The language to prefer for result text</param>
    /// <param name="page">The 1-based page of results to request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IEnumerable<MetadataSearchCandidate>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        int page = 1,
        CancellationToken cancellationToken = default);
}
