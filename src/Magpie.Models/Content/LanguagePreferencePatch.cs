using System;

namespace Magpie.Models.Content;

/// <summary>
/// Patch for <see cref="LanguagePreference"/>.
/// </summary>
public sealed record LanguagePreferencePatch
{
    /// <inheritdoc cref="LanguagePreference.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="LanguagePreference.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="LanguagePreference.Language" />
    public FieldPatch<string>? Language { get; init; }
}
