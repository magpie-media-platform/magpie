using System;

namespace Magpie.Data.Database.Entities;

public record CustomMetadataValue
{
    public Guid MetadataContributionId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public MetadataContribution? MetadataContribution { get; set; }
}
