namespace Magpie.Models.Filters;

/// <summary>
/// A range with optional minimum and maximum bounds.
/// </summary>
/// <typeparam name="T">The type of value in the range.</typeparam>
/// <param name="Min">The minimum value (inclusive), or null for no lower bound.</param>
/// <param name="Max">The maximum value (inclusive), or null for no upper bound.</param>
public sealed record Range<T>(T? Min, T? Max);
