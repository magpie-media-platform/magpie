using System;

namespace Magpie.Data.Database.Entities;

public class IndexerDefinitionCategory
{
    public Guid IndexerDefinitionId { get; set; }
    public required string Category { get; set; }

    public required IndexerDefinition IndexerDefinition { get; set; }
}
