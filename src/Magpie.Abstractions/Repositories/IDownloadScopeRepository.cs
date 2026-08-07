using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for download scope rows.
/// </summary>
public interface IDownloadScopeRepository :
    ICrudRepository<Guid, DownloadScope>,
    IQueryRepository<DownloadScope, DownloadScopeQuery>;
