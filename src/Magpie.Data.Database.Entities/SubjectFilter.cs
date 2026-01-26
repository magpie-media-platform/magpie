using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public record SubjectFilter
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Criteria { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ICollection<SubjectFilterTag> Tags { get; } = [];
}
