using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for event triggers.
/// </summary>
public interface IEventTriggerRepository :
    IQueryRepository<EventTrigger, EventTriggerQuery>,
    ICrudRepository<Guid, EventTrigger>,
    IPatchRepository<Guid, EventTrigger, EventTriggerPatch>;
