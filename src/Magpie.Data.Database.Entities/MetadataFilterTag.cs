using System;

namespace Magpie.Data.Database.Entities;

public class MetadataFilterTag
{
    public Guid MetadataFilterId { get; set; }
    public required string TagName { get; set; }

    public required MetadataFilter MetadataFilter { get; set; }
    public required Tag Tag { get; set; }
}
