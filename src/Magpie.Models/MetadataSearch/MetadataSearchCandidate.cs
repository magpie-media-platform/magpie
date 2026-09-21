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
    public required string ProviderType { get; set; }

    /// <summary>
    /// External ID source.
    /// </summary>
    public required string ExternalIdSource { get; set; }

    /// <summary>
    /// External ID.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// Content type slug.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// Content hierarchy level slug.
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// Display title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Release year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Release date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Overview text.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Language code.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Thumbnail URL.
    /// </summary>
    public Uri? ThumbnailUrl { get; set; }

    /// <summary>
    /// Related subjects.
    /// </summary>
    public IReadOnlyList<ProviderMetadataSearchSubjectReference> SubjectReferences { get; set; } = [];

    /// <summary>
    /// Other external ids the provider reports for the same real-world item.
    /// </summary>
    public IReadOnlyList<MetadataSearchCandidateExternalId> AdditionalExternalIds { get; set; } = [];
}
