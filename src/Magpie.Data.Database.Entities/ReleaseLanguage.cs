using System;

namespace Magpie.Data.Database.Entities;

public class ReleaseLanguage
{
    public Guid ReleaseId { get; set; }
    public required string Language { get; set; }

    public required Release Release { get; set; }
}
