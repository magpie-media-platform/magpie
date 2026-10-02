using System.Collections.Generic;
using System.Linq;

namespace Magpie.Models.MetadataSearch;

/// <summary>
/// The merged result of a metadata candidate search across providers.
/// </summary>
public sealed record ProviderMetadataCandidateSearchResult
{
    /// <summary>
    /// The reusable checkpoint for this page, including the first page.
    /// </summary>
    public string? Continuation { get; init; }

    /// <summary>
    /// The checkpoint for the next page, or null when all sources are exhausted.
    /// </summary>
    public string? NextContinuation { get; init; }

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
    /// The warnings for failed or incomplete searches.
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
