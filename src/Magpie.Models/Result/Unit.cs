namespace Magpie.Models.Result;

/// <summary>
/// Represents a non-value result.
/// </summary>
public sealed record Unit
{
    private Unit() { }
    /// <summary>
    /// The singleton instance of the Unit type.
    /// </summary>
    public static Unit Default { get; } = new();
}
