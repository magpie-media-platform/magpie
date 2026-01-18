using System;

namespace Magpie.Data.Database.Entities;

public class QualityTierListTag
{
    public Guid QualityTierListId { get; set; }
    public required string TagName { get; set; }

    public required QualityTierList QualityTierList { get; set; }
    public required Tag Tag { get; set; }
}
