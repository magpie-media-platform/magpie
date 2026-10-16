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
    IPatchRepository<Guid, ScheduleTrigger, ScheduleTriggerPatch>
{
    /// <summary>
    /// Creates a schedule trigger.
    /// </summary>
    /// <param name="model">The trigger to create</param>
    /// <param name="createdByUserId">
    /// The id of the user that creates the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new trigger</returns>
    Task<ScheduleTrigger> CreateAsync(
        ScheduleTrigger model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a schedule trigger by id.
    /// </summary>
    /// <param name="id">The id of the trigger to delete</param>
    /// <param name="deletedByUserId">
    /// The id of the user that deletes the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the trigger exists, otherwise <see langword="false"/></returns>
    Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks a schedule trigger until the current transaction ends.
    /// </summary>
    /// <param name="triggerId">The id of the schedule trigger.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    Task LockAsync(Guid triggerId, CancellationToken cancellationToken = default);

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
