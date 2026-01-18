using System;

namespace Magpie.Data.Database.Entities;

public class ReleaseProperty
{
    public Guid ReleaseId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required Release Release { get; set; }
}
