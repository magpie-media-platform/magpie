using System;

namespace Magpie.Models;

/// <summary>
/// A range of values.
/// </summary>
/// <typeparam name="T">The type of the values.</typeparam>
/// <param name="Min">The minimum value.</param>
/// <param name="Max">The maximum value.</param>
public record Range<T>(T? Min, T? Max) where T : struct, IComparable<T>;
