namespace Magpie.Models.Api;

/// <summary>
/// The request to set the external match for a library import candidate.
/// </summary>
public sealed record SetLibraryImportMatchRequest
{
    /// <summary>
    /// The provider type of the metadata source that supplied the match.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The namespace key of the external id.
    /// </summary>
    public required string ExternalIdKey { get; init; }

    /// <summary>
    /// The value of the external id for the matched item.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// The display title of the matched item.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The secondary title of the matched item.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// The URL of the image for the matched item.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// The slug of the content hierarchy level for the matched item.
    /// </summary>
    public string? LevelSlug { get; init; }
}
