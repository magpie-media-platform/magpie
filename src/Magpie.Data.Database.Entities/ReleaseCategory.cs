using System;

namespace Magpie.Data.Database.Entities;

public class ReleaseCategory
{
    public Guid ReleaseId { get; set; }
    public required string Category { get; set; }

    public required Release Release { get; set; }
}
