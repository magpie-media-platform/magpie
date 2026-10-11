using System;
using System.Collections.Generic;

using Magpie.Models.Filtering;

namespace Magpie.Models.MetadataSearch;

/// <summary>
/// A content candidate from a metadata provider.
/// </summary>
public sealed record MetadataSearchCandidate
{
    /// <summary>
    /// The provider type of the candidate.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The source of the external id.
    /// </summary>
    public required string ExternalIdSource { get; set; }

    /// <summary>
    /// The external id of the candidate.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The content type slug of the candidate.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// The content hierarchy level slug of the candidate.
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// The display title of the candidate.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The release year of the candidate.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// The release date of the candidate.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// The calendar precision of <see cref="ReleaseDate"/>.
    /// </summary>
    public TemporalPrecision? ReleaseDatePrecision { get; set; }

    /// <summary>
    /// The overview text of the candidate.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// The language code of the candidate.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The URL of the thumbnail image.
    /// </summary>
    public Uri? ThumbnailUrl { get; set; }

    /// <summary>
    /// The related subjects of the candidate.
    /// </summary>
    public IReadOnlyList<ProviderMetadataSearchSubjectReference> SubjectReferences { get; set; } = [];

    /// <summary>
    /// The other external ids that the provider reports for the same item.
    /// </summary>
    public IReadOnlyList<MetadataSearchCandidateExternalId> AdditionalExternalIds { get; set; } = [];
}
