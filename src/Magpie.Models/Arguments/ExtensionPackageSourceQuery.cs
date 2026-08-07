namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for extension package sources.
/// </summary>
public sealed record ExtensionPackageSourceQuery
{
    /// <summary>
    /// The enabled state to match.
    /// </summary>
    public bool? Enabled { get; init; }
}
