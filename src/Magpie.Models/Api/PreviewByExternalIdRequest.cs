namespace Magpie.Models.Api;

/// <summary>
/// The request to get a content preview for an external id.
/// </summary>
public sealed record PreviewByExternalIdRequest
{
    /// <summary>
    /// The provider type of the external metadata source.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The namespace key of the external id.
    /// </summary>
    public required string ExternalIdKey { get; init; }

    /// <summary>
    /// The value of the external id.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// The slug of the content type.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// The slug of the content hierarchy level.
    /// </summary>
    public string? LevelSlug { get; init; }
}
