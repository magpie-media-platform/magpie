using System;

namespace Magpie.Data.Database.Entities;

public record QualityTierListTag
{
    public Guid QualityTierListId { get; set; }
    public required string TagName { get; set; }

    public QualityTierList? QualityTierList { get; set; }
    public Tag? Tag { get; set; }
}
