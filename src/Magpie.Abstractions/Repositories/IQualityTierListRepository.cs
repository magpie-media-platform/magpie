using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for quality tier lists.
/// </summary>
public interface IQualityTierListRepository :
    ICrudRepository<Guid, QualityTierList>,
    IPatchRepository<Guid, QualityTierList, QualityTierListPatch>;
