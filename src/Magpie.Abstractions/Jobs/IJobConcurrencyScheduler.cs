using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// The scheduler contract for job admission and concurrency.
/// </summary>
public interface IJobConcurrencyScheduler
{
    /// <summary>
    /// The total number of job slots.
    /// </summary>
    int Total { get; }

    /// <summary>
    /// The number of slots for general jobs.
    /// </summary>
    int GeneralCapacity { get; }

    /// <summary>
    /// The number of slots for fast-lane jobs.
    /// </summary>
    int FastLaneCapacity { get; }

    /// <summary>
    /// The number of jobs that run now.
    /// </summary>
    int InFlight { get; }

    /// <summary>
    /// Determines whether a free slot is available.
    /// </summary>
    bool HasAdmissionCapacity();

    /// <summary>
    /// Selects the next job to admit.
    /// </summary>
    /// <param name="candidates">
    /// The jobs that wait for a slot
    /// </param>
    /// <param name="excluded">
    /// The ids of the jobs to skip
    /// </param>
    /// <param name="selected">
    /// The job to admit, set only when the method returns true
    /// </param>
    bool TrySelectAdmission(
        IReadOnlyList<JobClaimCandidate> candidates,
        ISet<Guid> excluded,
        out JobClaimCandidate selected);

    /// <summary>
    /// Takes the slot of an admitted job.
    /// </summary>
    /// <param name="candidate">
    /// The job that takes the slot
    /// </param>
    void CommitAdmission(JobClaimCandidate candidate);

    /// <summary>
    /// Frees the slot of a job.
    /// </summary>
    /// <param name="id">
    /// The id of the job that frees its slot
    /// </param>
    void Release(Guid id);

    /// <summary>
    /// Rebalances the admitted jobs against the current UTC time.
    /// </summary>
    IReadOnlyList<Guid> Rebalance();

    /// <summary>
    /// Takes a slot for immediate use and gets the handle that frees it.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IDisposable> AcquireImmediateSlotAsync(CancellationToken cancellationToken = default);
}
