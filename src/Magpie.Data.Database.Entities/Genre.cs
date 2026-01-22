using System;

namespace Magpie.Data.Database.Entities;

public record Genre
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string GenreName { get; set; }

    public MetadataContribution? MetadataContribution { get; set; }
}
