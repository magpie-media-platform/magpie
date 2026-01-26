using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Triggers;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing schedule triggers.
/// </summary>
public interface IScheduleTriggerRepository :
    ICrudRepository<Guid, ScheduleTrigger>,
    IPatchRepository<Guid, ScheduleTrigger, ScheduleTriggerPatch>
{
    /// <summary>
    /// Get a schedule trigger by name.
    /// </summary>
    /// <param name="name">The trigger name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ScheduleTrigger?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all schedule triggers for a specific job template.
    /// </summary>
    /// <param name="jobTemplateId">The job template identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<ScheduleTrigger>>> GetByJobTemplateIdAsync(
        Guid jobTemplateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled schedule triggers.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<ScheduleTrigger>>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the last run time and next run time for a schedule trigger.
    /// </summary>
    /// <param name="id">The trigger identifier.</param>
    /// <param name="lastRunAt">The time the trigger last fired.</param>
    /// <param name="nextRunAt">The time the trigger will next fire.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the trigger was updated,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> UpdateRunTimesAsync(
        Guid id,
        DateTimeOffset? lastRunAt,
        DateTimeOffset? nextRunAt,
        CancellationToken cancellationToken = default);
}
