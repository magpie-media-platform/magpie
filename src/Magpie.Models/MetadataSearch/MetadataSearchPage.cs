using System.Collections.Generic;

namespace Magpie.Models.MetadataSearch;

/// <summary>
/// One page from a metadata provider search.
/// </summary>
public sealed record MetadataSearchPage
{
    /// <summary>
    /// An unsupported search result.
    /// </summary>
    public static MetadataSearchPage NotSupported => new()
    {
        Supported = false,
        Matches = [],
        PageInfo = ProviderSearchPageInfo.Unsupported
    };

    /// <summary>
    /// The warnings for failed or incomplete searches.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];

    /// <summary>
    /// Whether the provider supports the search.
    /// </summary>
    public required bool Supported { get; set; }

    /// <summary>
    /// The search candidates.
    /// </summary>
    public required IReadOnlyList<MetadataSearchCandidate> Matches { get; set; }

    /// <summary>
    /// The paging state of the search.
    /// </summary>
    public required ProviderSearchPageInfo PageInfo { get; set; }
}
