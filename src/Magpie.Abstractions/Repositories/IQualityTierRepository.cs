using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing quality tiers.
/// </summary>
public interface IQualityTierRepository :
    ICrudRepository<Guid, QualityTier>,
    IPatchRepository<Guid, QualityTier, QualityTierPatch>
{
}
