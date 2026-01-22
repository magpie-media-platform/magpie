using System;

namespace Magpie.Data.Database.Entities;

public record IndexerTag
{
    public Guid IndexerId { get; set; }
    public required string TagName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Indexer? Indexer { get; set; }
    public Tag? Tag { get; set; }
}
