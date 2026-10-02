using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The schema of one field in a setting schema row.
/// </summary>
public sealed record SettingSchemaField
{
    /// <summary>
    /// The key of the field.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The data type of the field value.
    /// </summary>
    public required DataType Type { get; set; }

    /// <summary>
    /// The display name of the field.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The description of the field.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the field is mandatory.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Whether the field holds a secret value.
    /// </summary>
    public bool Secret { get; set; }

    /// <summary>
    /// The value that applies when the user gives none.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// The values that the user can select.
    /// </summary>
    public IReadOnlyList<string>? Options { get; set; }

    /// <summary>
    /// The placeholder text for the input.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// The dynamic source of the options.
    /// </summary>
    public SettingSchemaOptionsSource? OptionsSource { get; set; }
}
