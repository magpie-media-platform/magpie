using System;

namespace Magpie.Data.Database.Entities;

public class ReleaseTag
{
    public Guid ReleaseId { get; set; }
    public required string TagName { get; set; }

    public required Release Release { get; set; }
    public required Tag Tag { get; set; }
}
