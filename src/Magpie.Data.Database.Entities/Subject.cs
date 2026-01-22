using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public record Subject
{
    public Guid Id { get; set; }
    public required string Kind { get; set; }
    public DateTimeOffset? DateLastRefreshed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public MetadataContainer? MetadataContainer { get; set; }
    public ICollection<SubjectTag> Tags { get; } = [];
}
