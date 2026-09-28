using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// A registered rename token and its value on the selected content.
/// </summary>
public sealed record RenameTokenInfo
{
    /// <summary>
    /// The token name to write between braces.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The data source of the token.
    /// </summary>
    public RenameTokenFamily Family { get; init; }

    /// <summary>
    /// The meaning of the token.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// An example value in the output format of the token.
    /// </summary>
    public required string Example { get; init; }

    /// <summary>
    /// The value on the selected content, or null when the value is missing or no content is selected.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Whether the selected content has a value, or null when no content is selected.
    /// </summary>
    public bool? Available { get; init; }
}
