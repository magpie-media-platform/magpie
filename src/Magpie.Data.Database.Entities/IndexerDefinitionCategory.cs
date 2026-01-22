using System;

namespace Magpie.Data.Database.Entities;

public record IndexerDefinitionCategory
{
    public Guid IndexerDefinitionId { get; set; }
    public required string Category { get; set; }

    public IndexerDefinition? IndexerDefinition { get; set; }
}
