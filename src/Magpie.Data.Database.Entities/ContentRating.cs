using System;

namespace Magpie.Data.Database.Entities;

public record ContentRating
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string RatingsBody { get; set; }
    public required string Value { get; set; }

    public MetadataContribution? MetadataContribution { get; set; }
}
