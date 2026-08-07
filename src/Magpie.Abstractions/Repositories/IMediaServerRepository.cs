using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for media servers.
/// </summary>
public interface IMediaServerRepository :
    ICrudRepository<Guid, MediaServer>,
    IPatchRepository<Guid, MediaServer, MediaServerPatch>;
