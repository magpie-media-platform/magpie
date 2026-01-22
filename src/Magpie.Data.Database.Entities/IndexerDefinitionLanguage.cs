using System;

namespace Magpie.Data.Database.Entities;

public record IndexerDefinitionLanguage
{
    public Guid IndexerDefinitionId { get; set; }
    public required string Language { get; set; }

    public IndexerDefinition? IndexerDefinition { get; set; }
}
