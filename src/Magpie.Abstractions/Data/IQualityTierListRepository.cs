using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.QualityTiers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing quality tier lists.
/// </summary>
public interface IQualityTierListRepository :
    ICrudRepository<Guid, QualityTierList>,
    IPatchRepository<Guid, QualityTierList, QualityTierListPatch>
{
    /// <summary>
    /// Get a quality tier list by name.
    /// </summary>
    /// <param name="name">The quality tier list name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<QualityTierList?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
