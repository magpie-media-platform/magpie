using System;

namespace Magpie.Data.Database.Entities;

public class ContentItemTag
{
    public Guid ContentItemId { get; set; }
    public required string TagName { get; set; }

    public required ContentItem ContentItem { get; set; }
    public required Tag Tag { get; set; }
}
