using System;

namespace Magpie.Data.Database.Entities;

public record ReleaseLanguage
{
    public Guid ReleaseId { get; set; }
    public required string Language { get; set; }

    public Release? Release { get; set; }
}
