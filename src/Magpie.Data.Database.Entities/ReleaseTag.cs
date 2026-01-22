using System;

namespace Magpie.Data.Database.Entities;

public record ReleaseTag
{
    public Guid ReleaseId { get; set; }
    public required string TagName { get; set; }

    public Release? Release { get; set; }
    public Tag? Tag { get; set; }
}
