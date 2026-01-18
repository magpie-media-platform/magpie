using System;

namespace Magpie.Data.Database.Entities;

public class Genre
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string GenreName { get; set; }

    public required MetadataContribution MetadataContribution { get; set; }
}
