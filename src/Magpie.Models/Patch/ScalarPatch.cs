namespace Magpie.Models.Patch;

/// <summary>
/// A patch operation for a property.
/// </summary>
/// <typeparam name="T">The type of value being patched.</typeparam>
/// <param name="Value">The value to set.</param>
public sealed record ScalarPatch<T>(T Value);
