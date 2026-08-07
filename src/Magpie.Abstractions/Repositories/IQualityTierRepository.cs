using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for quality tiers.
/// </summary>
public interface IQualityTierRepository :
    ICrudRepository<Guid, QualityTier>,
    IPatchRepository<Guid, QualityTier, QualityTierPatch>
{
}
