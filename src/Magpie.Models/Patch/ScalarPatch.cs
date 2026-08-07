namespace Magpie.Models.Patch;

/// <summary>
/// The patch for one property value.
/// </summary>
/// <typeparam name="T">The type of the value to patch.</typeparam>
public sealed record ScalarPatch<T>
{
    /// <summary>
    /// The value to set.
    /// </summary>
    public required T Value { get; init; }
}
