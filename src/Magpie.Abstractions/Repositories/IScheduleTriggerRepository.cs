using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing schedule triggers.
/// </summary>
public interface IScheduleTriggerRepository :
    ICrudRepository<Guid, ScheduleTrigger>,
    IPatchRepository<Guid, ScheduleTrigger, ScheduleTriggerPatch>
{
    /// <summary>
    /// List triggers that are due to fire by the specified time.
    /// </summary>
    /// <param name="asOf">The point in time to check for due triggers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<ScheduleTrigger>> ListDueTriggersAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claim a trigger's next run using optimistic concurrency.
    /// </summary>
    /// <param name="triggerId">The trigger id.</param>
    /// <param name="expectedNextRun">The expected next run time.</param>
    /// <param name="newNextRun">The new next run time to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if claimed, <see langword="false"/> if the next run time changed.</returns>
    Task<bool> TryClaimNextRunAsync(
        Guid triggerId,
        DateTime expectedNextRun,
        DateTime newNextRun,
        CancellationToken cancellationToken = default);
}
