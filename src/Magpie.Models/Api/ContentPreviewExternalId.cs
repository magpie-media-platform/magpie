namespace Magpie.Models.Api;

/// <summary>
/// An external id entry in a <see cref="ContentPreview"/>.
/// </summary>
public sealed record ContentPreviewExternalId
{
    /// <summary>
    /// The slug that names the external id source.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// The value of the external id.
    /// </summary>
    public required string Value { get; init; }
}
