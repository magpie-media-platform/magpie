using System;

namespace Magpie.Data.Database.Entities;

public record ContentItemTag
{
    public Guid ContentItemId { get; set; }
    public required string TagName { get; set; }

    public ContentItem? ContentItem { get; set; }
    public Tag? Tag { get; set; }
}
