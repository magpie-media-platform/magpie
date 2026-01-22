using System;

namespace Magpie.Data.Database.Entities;

public record SubjectTag
{
    public Guid SubjectId { get; set; }
    public required string TagName { get; set; }

    public Subject? Subject { get; set; }
    public Tag? Tag { get; set; }
}
