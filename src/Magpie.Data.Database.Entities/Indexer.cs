using System;
using System.Collections.Generic;

using Magpie.Models.Indexers;

namespace Magpie.Data.Database.Entities;

public class Indexer
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public required string Name { get; set; }
    public bool Enabled { get; set; }
    public IndexerStatus Status { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public required IndexerDefinition Definition { get; set; }
    public ICollection<IndexerSetting> Settings { get; } = [];
    public ICollection<IndexerTag> Tags { get; } = [];
    public ICollection<Release> Releases { get; } = [];
}
