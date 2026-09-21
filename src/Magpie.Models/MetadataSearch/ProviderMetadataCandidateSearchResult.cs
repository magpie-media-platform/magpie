using System.Collections.Generic;
using System.Linq;

namespace Magpie.Models.MetadataSearch;

/// <summary>
/// The merged result of a metadata candidate search across providers.
/// </summary>
public sealed record ProviderMetadataCandidateSearchResult
{
    /// <summary>
    /// A result with no candidates.
    /// </summary>
    public static ProviderMetadataCandidateSearchResult Empty => new()
    {
        Matches = [],
        PageItemCount = 0,
        PageInfo = ProviderSearchPageInfo.Unsupported
    };

    /// <summary>
    /// Warnings for failed or incomplete searches, not valid empty results.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// The candidates that match the search criteria.
    /// </summary>
    public IReadOnlyList<MetadataSearchMatch> Matches { get; init; } = [];

    /// <summary>
    /// The number of items on the provider search page, before criteria filtering.
    /// </summary>
    public required int PageItemCount { get; init; }

    /// <summary>
    /// The number of matches that already exist in the library.
    /// </summary>
    public int ExistsInLibraryCount => Matches.Count(match => match.InLibrary);

    /// <summary>
    /// The paging state merged across the providers of the search.
    /// </summary>
    public required ProviderSearchPageInfo PageInfo { get; init; }
}
