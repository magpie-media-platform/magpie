using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Alternate text for content.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="Type">The text type.</param>
/// <param name="Language">The language code.</param>
/// <param name="Text">The text content.</param>
public sealed record AlternateTextMetadata(
    Guid Id,
    int Ordinal,
    string Type,
    string? Language,
    string Text);
