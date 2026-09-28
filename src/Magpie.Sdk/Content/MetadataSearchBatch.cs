using Magpie.Models.MetadataSearch;

namespace Magpie.Sdk.Content;

/// <summary>
/// A batch of verified candidates and a reusable provider checkpoint.
/// </summary>
public sealed record MetadataSearchBatch
{
    /// <summary>
    /// The candidates, warnings, and source exhaustion state.
    /// </summary>
    public MetadataSearchPage Page { get; set; } = MetadataSearchPage.NotSupported;

    /// <summary>
    /// The provider-owned checkpoint for the next batch, without previously delivered responses.
    /// </summary>
    public object? Continuation { get; set; }
}
