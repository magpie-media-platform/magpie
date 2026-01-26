using System;

namespace Magpie.Data.Database.Entities;

public record SubjectFilterTag
{
    public Guid SubjectFilterId { get; set; }
    public required string TagName { get; set; }

    public SubjectFilter? SubjectFilter { get; set; }
    public Tag? Tag { get; set; }
}
