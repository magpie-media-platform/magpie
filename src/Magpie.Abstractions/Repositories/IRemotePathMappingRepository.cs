using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for remote path mappings.
/// </summary>
public interface IRemotePathMappingRepository :
    ICrudRepository<Guid, RemotePathMapping>,
    IPatchRepository<Guid, RemotePathMapping, RemotePathMappingPatch>
{
}
