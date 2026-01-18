using System;

namespace Magpie.Data.Database.Entities;

public class IndexerFeedSetting
{
    public Guid Id { get; set; }
    public Guid IndexerFeedId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required IndexerFeed IndexerFeed { get; set; }
}
