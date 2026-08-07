using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for schedule triggers.
/// </summary>
public interface IScheduleTriggerRepository :
    IQueryRepository<ScheduleTrigger, ScheduleTriggerQuery>,
    ICrudRepository<Guid, ScheduleTrigger>,
    IPatchRepository<Guid, ScheduleTrigger, ScheduleTriggerPatch>
{
    /// <summary>
    /// Claims the next run of a trigger with optimistic concurrency, in a single atomic step.
    /// </summary>
    /// <param name="triggerId">The id of the schedule trigger</param>
    /// <param name="expectedNextRun">The next run time that the stored trigger must hold</param>
    /// <param name="newNextRun">The next run time to store</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when this call wins the claim, otherwise <see langword="false"/></returns>
    Task<bool> TryClaimNextRunAsync(
        Guid triggerId,
        DateTime expectedNextRun,
        DateTime newNextRun,
        CancellationToken cancellationToken = default);
}
