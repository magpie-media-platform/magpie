using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class RootFolder
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public Guid ContentTypeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ICollection<ContentHierarchyLevel> ContentHierarchyLevels { get; } = [];
}
