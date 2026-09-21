namespace Magpie.Models;

/// <summary>
/// One selectable option in a setting schema.
/// </summary>
public sealed record SettingSchemaOption
{
    /// <summary>
    /// The value that the option stores.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// The label that the UI shows for the option.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// The description of the option.
    /// </summary>
    public string? Description { get; set; }
}
