using System;

namespace Magpie.Models;

/// <summary>
/// An inclusive minimum-to-maximum range.
/// </summary>
/// <typeparam name="T">The comparable value type of the bounds.</typeparam>
/// <param name="Min">The inclusive lower bound, or null for no lower limit</param>
/// <param name="Max">The inclusive upper bound, or null for no upper limit</param>
public record Range<T>(T? Min, T? Max) where T : struct, IComparable<T>;
