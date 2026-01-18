using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class IndexerFeed
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Provider { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset? LastSync { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ICollection<IndexerFeedSetting> Settings { get; } = [];
    public ICollection<IndexerDefinition> Definitions { get; } = [];
}
