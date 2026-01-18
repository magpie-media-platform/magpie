using System;

namespace Magpie.Data.Database.Entities;

public class Award
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string AwardBody { get; set; }
    public required string Name { get; set; }
    public string? Category { get; set; }
    public string? Status { get; set; }
    public int? Year { get; set; }
    public string? Subject { get; set; }
    public string? Language { get; set; }
    public string? Country { get; set; }

    public required MetadataContribution MetadataContribution { get; set; }
}
