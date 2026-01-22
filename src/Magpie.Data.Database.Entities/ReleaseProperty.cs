using System;

namespace Magpie.Data.Database.Entities;

public record ReleaseProperty
{
    public Guid ReleaseId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public Release? Release { get; set; }
}
