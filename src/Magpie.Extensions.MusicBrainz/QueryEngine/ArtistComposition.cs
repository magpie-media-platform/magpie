using Magpie.Models.Enums;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ArtistComposition
{
    public required ArtistCompositionEntity RelatedEntity { get; init; }

    public required ArtistCompositionResolution Resolution { get; init; }

    public required ArtistCompositionExpansion Expansion { get; init; }

    public required ArtistCompositionConstraint Constraint { get; init; }

    public required ArtistCompositionFinalStage FinalStage { get; init; }

    public LookupCapability ExpansionCapabilities { get; init; }

    public required string ExternalIdSource { get; init; }

    public string? RelatedMbid { get; init; }

    public string? RelatedName { get; init; }

    public CriteriaValueOperator RelatedNameOperator { get; init; } = CriteriaValueOperator.Equals;

    public string? RelatedArtistType { get; init; }

    public string? AlternativeGroup { get; init; }
}
