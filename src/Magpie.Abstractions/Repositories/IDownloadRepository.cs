using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing downloads.
/// </summary>
public interface IDownloadRepository : ICrudRepository<Guid, Download>, IQueryRepository<Download, DownloadQuery>
{
}
