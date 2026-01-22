using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public record MetadataContainer
{
    public Guid Id { get; set; }
    public DateTimeOffset DateAdded { get; set; }
    public Guid? ContentHierarchyId { get; set; }
    public Guid? SubjectId { get; set; }

    public ContentHierarchyLevel? ContentHierarchyLevel { get; set; }
    public Subject? Subject { get; set; }
    public ICollection<MetadataContribution> Contributions { get; } = [];
}
