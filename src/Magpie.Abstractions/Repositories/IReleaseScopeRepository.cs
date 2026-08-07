using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for release scope rows.
/// </summary>
public interface IReleaseScopeRepository :
    ICrudRepository<Guid, ReleaseScope>,
    IQueryRepository<ReleaseScope, ReleaseScopeQuery>;
