using System;
using System.Collections.Generic;
using System.Globalization;

namespace Magpie.Models.Signals;

/// <summary>
/// Reads values from content-neutral signal maps.
/// </summary>
public static class SignalBagExtensions
{
    /// <summary>
    /// Reads all values for a signal key.
    /// </summary>
    /// <param name="signals">The signal values</param>
    /// <param name="key">The signal key</param>
    /// <returns>The signal values, or an empty list when the key is absent</returns>
    public static IReadOnlyList<string> GetValues(
        this IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        string key)
    {
        return signals.TryGetValue(NormalizeKey(key), out var values) ? values : [];
    }

    /// <summary>
    /// Reads the first value for a signal key.
    /// </summary>
    /// <param name="signals">The signal values</param>
    /// <param name="key">The signal key</param>
    /// <returns>The first signal value, or <see langword="null"/> when the key is absent</returns>
    public static string? GetFirstValue(
        this IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        string key)
    {
        return signals.TryGetValue(NormalizeKey(key), out var values) && values.Count > 0 ? values[0] : null;
    }

    /// <summary>
    /// Reads the first integer value for a signal key.
    /// </summary>
    /// <param name="signals">The signal values</param>
    /// <param name="key">The signal key</param>
    /// <returns>The first integer signal value, or <see langword="null"/> when unavailable</returns>
    public static int? GetFirstInt32(
        this IReadOnlyDictionary<string, IReadOnlyList<string>> signals,
        string key)
    {
        var value = signals.GetFirstValue(key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A signal key is required.", nameof(key));
        }

        return key.Trim();
    }
}
