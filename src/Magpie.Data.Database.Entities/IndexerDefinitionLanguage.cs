using System;

namespace Magpie.Data.Database.Entities;

public class IndexerDefinitionLanguage
{
    public Guid IndexerDefinitionId { get; set; }
    public required string Language { get; set; }

    public required IndexerDefinition IndexerDefinition { get; set; }
}
