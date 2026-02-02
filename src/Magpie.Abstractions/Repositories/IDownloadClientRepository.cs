using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing download client configurations.
/// </summary>
public interface IDownloadClientRepository :
    ICrudRepository<Guid, DownloadClient>,
    IPatchRepository<Guid, DownloadClient, DownloadClientPatch>
{
}
