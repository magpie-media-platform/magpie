using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Magpie.Models.Signals;

/// <summary>
/// Builds a content-neutral, string-keyed multimap of signal values.
/// </summary>
public sealed class SignalBagBuilder
{
    private readonly Dictionary<string, List<string>> _signals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes an empty signal bag builder.
    /// </summary>
    public SignalBagBuilder()
    {
    }

    /// <summary>
    /// Adds a non-empty value to a signal key.
    /// </summary>
    /// <param name="key">The signal key</param>
    /// <param name="value">The signal value</param>
    /// <returns>The current signal bag builder</returns>
    public SignalBagBuilder AddValue(string key, string? value)
    {
        var signalKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return this;
        }

        if (!_signals.TryGetValue(signalKey, out var values))
        {
            values = [];
            _signals[signalKey] = values;
        }

        values.Add(value.Trim());
        return this;
    }

    /// <summary>
    /// Adds a non-empty integer value to a signal key.
    /// </summary>
    /// <param name="key">The signal key</param>
    /// <param name="value">The signal value</param>
    /// <returns>The current signal bag builder</returns>
    public SignalBagBuilder AddValue(string key, int? value)
    {
        return value is { } present
            ? AddValue(key, present.ToString(CultureInfo.InvariantCulture))
            : this;
    }

    /// <summary>
    /// Builds an immutable snapshot of the signal values.
    /// </summary>
    /// <returns>The built signal values</returns>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Build()
    {
        var snapshot = new Dictionary<string, IReadOnlyList<string>>(_signals.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in _signals)
        {
            snapshot[key] = new List<string>(values).AsReadOnly();
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(snapshot);
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
