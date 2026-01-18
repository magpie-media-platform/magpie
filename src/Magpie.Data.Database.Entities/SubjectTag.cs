using System;

namespace Magpie.Data.Database.Entities;

public class SubjectTag
{
    public Guid SubjectId { get; set; }
    public required string TagName { get; set; }

    public required Subject Subject { get; set; }
    public required Tag Tag { get; set; }
}
