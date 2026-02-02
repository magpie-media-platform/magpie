using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing releases.
/// </summary>
public interface IReleaseRepository : ICrudRepository<Guid, Release>, IQueryRepository<Release, ReleaseQuery>
{
}
