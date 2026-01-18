using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// A genre for a metadata item.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="GenreName">The genre name.</param>
public sealed record GenreMetadata(
    Guid Id,
    int Ordinal,
    string GenreName);
