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
    public required string Key { get; init; }

    /// <summary>
    /// The data type of the field value.
    /// </summary>
    public required DataType Type { get; init; }

    /// <summary>
    /// The display name of the field.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The description of the field.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the field is mandatory.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Whether the field holds a secret value.
    /// </summary>
    public bool Secret { get; init; }

    /// <summary>
    /// The value that applies when the user gives none.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// The values that the user can select.
    /// </summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>
    /// The placeholder text for the input.
    /// </summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// The dynamic source of the options.
    /// </summary>
    public SettingSchemaOptionsSource? OptionsSource { get; init; }
}
