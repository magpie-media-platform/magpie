using System;

namespace Magpie.Data.Database.Entities;

public record LanguagePreference
{
    public Guid Id { get; set; }
    public Guid ContentHierarchyId { get; set; }
    public int Ordinal { get; set; }
    public required string Language { get; set; }

    public ContentHierarchyLevel? ContentHierarchyLevel { get; set; }
}
