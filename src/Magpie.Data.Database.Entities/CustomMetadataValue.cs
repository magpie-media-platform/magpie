using System;

namespace Magpie.Data.Database.Entities;

public class CustomMetadataValue
{
    public Guid MetadataContributionId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required MetadataContribution MetadataContribution { get; set; }
}
