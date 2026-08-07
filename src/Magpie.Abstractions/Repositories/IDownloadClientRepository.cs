using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for download clients.
/// </summary>
public interface IDownloadClientRepository :
    ICrudRepository<Guid, DownloadClient>,
    IPatchRepository<Guid, DownloadClient, DownloadClientPatch>
{
}
