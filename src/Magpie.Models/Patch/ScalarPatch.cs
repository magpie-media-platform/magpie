namespace Magpie.Models.Patch;

/// <summary>
/// A patch operation for a property.
/// </summary>
/// <typeparam name="T">The type of value being patched.</typeparam>
public sealed record ScalarPatch<T>
{
    /// <summary>
    /// The value to set.
    /// </summary>
    public required T Value { get; init; }
}
