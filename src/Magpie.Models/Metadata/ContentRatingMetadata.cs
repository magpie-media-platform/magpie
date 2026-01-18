using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// A content rating from a ratings body.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="RatingsBody">The ratings body that issued this rating.</param>
/// <param name="Value">The rating value.</param>
public sealed record ContentRatingMetadata(
    Guid Id,
    int Ordinal,
    string RatingsBody,
    string Value);
