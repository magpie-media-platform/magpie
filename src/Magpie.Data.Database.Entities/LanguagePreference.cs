using System;

namespace Magpie.Data.Database.Entities;

public class LanguagePreference
{
    public Guid Id { get; set; }
    public Guid ContentHierarchyId { get; set; }
    public int Ordinal { get; set; }
    public required string Language { get; set; }

    public required ContentHierarchyLevel ContentHierarchyLevel { get; set; }
}
