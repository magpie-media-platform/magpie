using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The dynamic source of the options for a setting schema.
/// </summary>
public sealed record SettingSchemaOptionsSource
{
    /// <summary>
    /// The kind of the option source.
    /// </summary>
    public required SettingOptionsSourceKind Kind { get; init; }

    /// <summary>
    /// The name of the JavaScript function that supplies the options.
    /// </summary>
    public string? FunctionName { get; init; }
}
