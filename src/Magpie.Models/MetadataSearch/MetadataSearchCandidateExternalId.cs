namespace Magpie.Models.MetadataSearch;

/// <summary>
/// A cross-provider external id known for a <see cref="MetadataSearchCandidate"/>.
/// </summary>
public sealed record MetadataSearchCandidateExternalId
{
    /// <summary>
    /// The name of the source that supplies the external id.
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// The value of the external id.
    /// </summary>
    public required string Value { get; set; }
}
