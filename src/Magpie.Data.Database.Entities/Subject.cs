using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class Subject
{
    public Guid Id { get; set; }
    public required string Kind { get; set; }
    public DateTimeOffset? DateLastRefreshed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public required MetadataContainer MetadataContainer { get; set; }
    public ICollection<SubjectTag> Tags { get; } = [];
}
