using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// An external reference or link for content.
/// </summary>
/// <remarks>
/// References are external resources like Wikipedia pages, IMDb entries, official websites,
/// or other informational links that provide additional context.
/// </remarks>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="Kind">The kind of reference (e.g., "homepage", "biography", "social").</param>
/// <param name="Url">The URL of the external reference.</param>
/// <param name="DisplayName">The display name for the reference, if different from the type.</param>
/// <param name="Language">The language code, if applicable.</param>
public sealed record ReferenceMetadata(
    Guid Id,
    int Ordinal,
    string Kind,
    Uri Url,
    string? DisplayName,
    string? Language);
