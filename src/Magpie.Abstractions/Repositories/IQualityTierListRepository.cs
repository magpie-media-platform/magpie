using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

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
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<QualityTierList?>> ListByTagAsync(string tagName, CancellationToken cancellationToken = default);
}
