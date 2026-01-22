using System;

namespace Magpie.Data.Database.Entities;

public record ContentFilterTag
{
    public Guid ContentFilterId { get; set; }
    public required string TagName { get; set; }

    public ContentFilter? ContentFilter { get; set; }
    public Tag? Tag { get; set; }
}
