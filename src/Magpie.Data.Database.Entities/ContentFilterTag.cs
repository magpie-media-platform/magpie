using System;

namespace Magpie.Data.Database.Entities;

public class ContentFilterTag
{
    public Guid ContentFilterId { get; set; }
    public required string TagName { get; set; }

    public required ContentFilter ContentFilter { get; set; }
    public required Tag Tag { get; set; }
}
