using System;

namespace Magpie.Data.Database.Entities;

public record IndexerDefinitionTag
{
    public Guid IndexerDefinitionId { get; set; }
    public required string TagName { get; set; }

    public IndexerDefinition? IndexerDefinition { get; set; }
    public Tag? Tag { get; set; }
}
