namespace Magpie.Models.Content;

/// <summary>
/// Defines a custom metadata field.
/// </summary>
public sealed record CustomMetadataFieldDefinition
{
    /// <summary>
    /// The key of the custom metadata field.
    /// </summary>
    public required string Key { get; init; }
    /// <summary>
    /// The display name of the custom metadata field.
    /// </summary>
    public string? DisplayName { get; init; }
}
