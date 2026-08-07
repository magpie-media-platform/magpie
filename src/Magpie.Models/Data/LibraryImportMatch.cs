namespace Magpie.Models.Data;

/// <summary>
/// A metadata match for a library import candidate.
/// </summary>
public sealed record LibraryImportMatch
{
    /// <summary>
    /// The confidence of the match.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// The name of the provider type.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The name of the source of the external id.
    /// </summary>
    public required string ExternalIdKey { get; init; }

    /// <summary>
    /// The value of the external id.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// The title of the matched metadata.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The subtitle of the matched metadata.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// The URL of the image of the matched metadata.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// The slug of the content hierarchy level.
    /// </summary>
    public string? LevelSlug { get; init; }
}
