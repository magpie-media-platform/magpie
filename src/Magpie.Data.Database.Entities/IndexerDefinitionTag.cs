using System;

namespace Magpie.Data.Database.Entities;

public class IndexerDefinitionTag
{
    public Guid IndexerDefinitionId { get; set; }
    public required string TagName { get; set; }

    public required IndexerDefinition IndexerDefinition { get; set; }
    public required Tag Tag { get; set; }
}
