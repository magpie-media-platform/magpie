using System;

namespace Magpie.Data.Database.Entities;

public record ReleaseCategory
{
    public Guid ReleaseId { get; set; }
    public required string Category { get; set; }

    public Release? Release { get; set; }
}
