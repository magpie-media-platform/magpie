using System;

namespace Magpie.Data.Database.Entities;

public record AlternateText
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string Type { get; set; }
    public string? Language { get; set; }
    public required string Text { get; set; }

    public MetadataContribution? MetadataContribution { get; set; }
}
