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
    public static MetadataSearchPage NotSupported { get; } = new()
    {
        Supported = false,
        Matches = [],
        PageInfo = ProviderSearchPageInfo.Unsupported
    };

    /// <summary>
    /// Warning messages.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Whether the provider supports the search.
    /// </summary>
    public required bool Supported { get; init; }

    /// <summary>
    /// Search candidates.
    /// </summary>
    public required IReadOnlyList<MetadataSearchCandidate> Matches { get; init; }

    /// <summary>
    /// Paging state.
    /// </summary>
    public required ProviderSearchPageInfo PageInfo { get; init; }
}
