using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class Release
{
    public Guid Id { get; set; }
    public Guid? IndexerId { get; set; }
    public string? ExternalId { get; set; }
    public required string Provider { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset PublishDate { get; set; }
    public long? Size { get; set; }
    public string? InfoHash { get; set; }
    public required Uri DownloadUrl { get; set; }
    public Uri? InfoUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public Indexer? Indexer { get; set; }
    public ICollection<ReleaseCategory> Categories { get; } = [];
    public ICollection<ReleaseLanguage> Languages { get; } = [];
    public ICollection<ReleaseProperty> Properties { get; } = [];
    public ICollection<ReleaseTag> Tags { get; } = [];
    public ICollection<Download> Downloads { get; } = [];
}
