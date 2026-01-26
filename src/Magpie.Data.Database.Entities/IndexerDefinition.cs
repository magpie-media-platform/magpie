using System;
using System.Collections.Generic;

using Magpie.Models.Indexers;

namespace Magpie.Data.Database.Entities;

public record IndexerDefinition
{
    public Guid Id { get; set; }
    public Guid? FeedId { get; set; }
    public required string Provider { get; set; }
    public required string Name { get; set; }
    public IndexerAccess Access { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public IndexerFeed? Feed { get; set; }
    public ICollection<IndexerDefinitionCategory> Categories { get; } = [];
    public ICollection<IndexerDefinitionLanguage> Languages { get; } = [];
    public ICollection<IndexerDefinitionTag> Tags { get; } = [];
    public ICollection<Indexer> Indexers { get; } = [];
}

