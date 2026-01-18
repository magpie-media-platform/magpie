using System;

namespace Magpie.Data.Database.Entities;

public class Relationship
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public Guid SubjectId { get; set; }
    public int Ordinal { get; set; }
    public required string Relation { get; set; }
    public string? Qualifier { get; set; }

    public required MetadataContribution MetadataContribution { get; set; }
    public required Subject Subject { get; set; }
}
