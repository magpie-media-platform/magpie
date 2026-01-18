using System;

namespace Magpie.Data.Database.Entities;

public class IndexerTag
{
    public Guid IndexerId { get; set; }
    public required string TagName { get; set; }

    public required Indexer Indexer { get; set; }
    public required Tag Tag { get; set; }
}
