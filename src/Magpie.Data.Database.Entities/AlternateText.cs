using System;

namespace Magpie.Data.Database.Entities;

public class AlternateText
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string Type { get; set; }
    public string? Language { get; set; }
    public required string Text { get; set; }

    public required MetadataContribution MetadataContribution { get; set; }
}
