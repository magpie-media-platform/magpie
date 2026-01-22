using System;

namespace Magpie.Data.Database.Entities;

public record MetadataFilterTag
{
    public Guid MetadataFilterId { get; set; }
    public required string TagName { get; set; }

    public MetadataFilter? MetadataFilter { get; set; }
    public Tag? Tag { get; set; }
}
