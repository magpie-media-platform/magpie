using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing media server configurations.
/// </summary>
public interface IMediaServerRepository :
    ICrudRepository<Guid, MediaServer>,
    IPatchRepository<Guid, MediaServer, MediaServerPatch>
{
}
