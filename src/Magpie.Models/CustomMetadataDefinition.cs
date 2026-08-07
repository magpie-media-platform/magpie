using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The definition of a custom metadata field.
/// </summary>
public sealed record CustomMetadataDefinition
{
    /// <summary>
    /// The stable key of the field.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The data type of the field value.
    /// </summary>
    public DataType DataType { get; init; } = DataType.String;

    /// <summary>
    /// The display name of the field.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The description of the field.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The format hint that the UI uses for the value.
    /// </summary>
    public string? Format { get; init; }
}
