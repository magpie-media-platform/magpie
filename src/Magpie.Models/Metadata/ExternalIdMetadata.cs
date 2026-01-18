using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// An external identifier for content.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="Value">The identifier value.</param>
/// <param name="Attribution">The attribution for the identifier.</param>
public sealed record ExternalIdMetadata(
    Guid Id,
    int Ordinal,
    string Value,
    string? Attribution);
