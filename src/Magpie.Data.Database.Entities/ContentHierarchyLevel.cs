using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public record ContentHierarchyLevel
{
    public Guid Id { get; set; }
    public Guid ContentHierarchyLevelDefinitionId { get; set; }
    public Guid RootFolderId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? RelativePath { get; set; }
    public Guid? ParentId { get; set; }
    public DateTimeOffset? DateLastRefreshed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ContentHierarchyLevel? Parent { get; set; }
    public RootFolder? RootFolder { get; set; }
    public ICollection<ContentHierarchyLevel> Children { get; } = [];
    public ContentItem? ContentItem { get; set; }
    public MetadataContainer? MetadataContainer { get; set; }
    public ICollection<LanguagePreference> LanguagePreferences { get; } = [];
}
