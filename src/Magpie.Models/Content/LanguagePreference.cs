using System;

namespace Magpie.Models.Content;

/// <summary>
/// A ranked language preference.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The preference order (lower is higher priority).</param>
/// <param name="Language">The language code.</param>
public sealed record LanguagePreference(
    Guid Id,
    int Ordinal,
    string Language);
