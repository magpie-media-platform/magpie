using System;
using System.Collections.Generic;

namespace Magpie.Models.MetadataSearch;

/// <summary>
/// A content candidate from a metadata provider.
/// </summary>
public sealed record MetadataSearchCandidate
{
    /// <summary>
    /// Provider type.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// External ID source.
    /// </summary>
    public required string ExternalIdSource { get; init; }

    /// <summary>
    /// External ID.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Content type slug.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Content hierarchy level slug.
    /// </summary>
    public string? Level { get; init; }

    /// <summary>
    /// Display title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Release year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Release date.
    /// </summary>
    public DateTime? ReleaseDate { get; init; }

    /// <summary>
    /// Overview text.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Language code.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Thumbnail URL.
    /// </summary>
    public Uri? ThumbnailUrl { get; init; }

    /// <summary>
    /// Related subjects.
    /// </summary>
    public IReadOnlyList<ProviderMetadataSearchSubjectReference> SubjectReferences { get; init; } = [];
}
