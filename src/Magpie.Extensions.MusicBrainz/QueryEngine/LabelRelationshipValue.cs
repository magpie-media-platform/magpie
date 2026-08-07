namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record LabelRelationshipValue
{
    public string? LabelMbid { get; init; }

    public string? LabelName { get; init; }
}
