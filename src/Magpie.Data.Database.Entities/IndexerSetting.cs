using System;

namespace Magpie.Data.Database.Entities;

public class IndexerSetting
{
    public Guid Id { get; set; }
    public Guid IndexerId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required Indexer Indexer { get; set; }
}
