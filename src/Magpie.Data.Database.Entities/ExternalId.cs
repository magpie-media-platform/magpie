using System;

namespace Magpie.Data.Database.Entities;

public record ExternalId
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string Value { get; set; }
    public string? Attribution { get; set; }

    public MetadataContribution? MetadataContribution { get; set; }
}
