using Magpie.Models.Enums;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ArtistRelationshipValue
{
    public string? ArtistMbid { get; init; }

    public string? ArtistName { get; init; }

    public CriteriaValueOperator ArtistNameOperator { get; init; } = CriteriaValueOperator.Equals;

    public bool UseNameWhenMbidMissing { get; init; }

    public string? ArtistType { get; init; }
}
