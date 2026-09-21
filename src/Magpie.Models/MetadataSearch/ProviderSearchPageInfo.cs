namespace Magpie.Models.MetadataSearch;

/// <summary>
/// The paging state of a provider search.
/// </summary>
public sealed record ProviderSearchPageInfo
{
    /// <summary>
    /// The paging state for a provider that does not support the search.
    /// </summary>
    public static ProviderSearchPageInfo Unsupported => new()
    {
        RequestedPage = 1,
        HasMore = false
    };

    /// <summary>
    /// The 1-based number of the requested page.
    /// </summary>
    public required int RequestedPage { get; set; }

    /// <summary>
    /// Whether more results can exist. False only when the provider gives the end of the results.
    /// </summary>
    public required bool HasMore { get; set; }
}
