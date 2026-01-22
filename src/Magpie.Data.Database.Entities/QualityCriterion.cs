using System;

using Magpie.Models.QualityTiers;

namespace Magpie.Data.Database.Entities;

public record QualityCriterion
{
    public Guid Id { get; set; }
    public Guid QualityTierId { get; set; }
    public required string Field { get; set; }
    public QualityCriterionOperator Operator { get; set; }
    public required string Value { get; set; }

    public QualityTier? QualityTier { get; set; }
}
