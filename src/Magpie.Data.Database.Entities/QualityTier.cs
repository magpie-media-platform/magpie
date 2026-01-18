using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class QualityTier
{
    public Guid Id { get; set; }
    public Guid QualityTierListId { get; set; }
    public int Ordinal { get; set; }
    public required string Name { get; set; }
    public bool IsCutoff { get; set; }

    public required QualityTierList QualityTierList { get; set; }
    public ICollection<QualityCriterion> Criteria { get; } = [];
}
