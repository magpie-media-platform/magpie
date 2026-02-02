using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// Defines a custom metadata field.
/// </summary>
public sealed record CustomMetadataDefinition
{
    /// <summary>
    /// The key of the custom metadata field.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The data type of the custom metadata field.
    /// </summary>
    public DataType DataType { get; init; } = DataType.String;

    /// <summary>
    /// The display name of the custom metadata field.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The description of the custom metadata field.
    /// </summary>
    public string? Description { get; init; }
}
