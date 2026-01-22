namespace Magpie.Models;

/// <summary>
/// A patch operation for a field.
/// </summary>
/// <typeparam name="T">The type of value being patched.</typeparam>
/// <param name="Value">The value to set, or null to clear the field.</param>
public sealed record FieldPatch<T>(T Value);
