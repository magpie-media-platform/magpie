using System;

namespace Magpie.Data.Database.Entities;

public record Reference
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string Kind { get; set; }
    public required Uri Url { get; set; }
    public string? DisplayName { get; set; }
    public string? Language { get; set; }

    public MetadataContribution? MetadataContribution { get; set; }
}
