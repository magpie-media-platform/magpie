using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Jobs;

namespace Magpie.Models;

/// <summary>
/// The catalog entry for a job type.
/// </summary>
public sealed record JobSpec
{
    /// <summary>
    /// The stable type identifier of the job.
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// The display name of the job.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The description of the job.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The category of the job.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// The scope that the job operates on.
    /// </summary>
    public required JobScope Scope { get; init; }

    /// <summary>
    /// Whether the job is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Whether a user can start the job on demand.
    /// </summary>
    public bool OnDemand { get; init; }

    /// <summary>
    /// Whether the job can run immediately instead of from the queue.
    /// </summary>
    public bool SupportsImmediate { get; init; } = true;

    /// <summary>
    /// The policy that applies when an execution fails.
    /// </summary>
    public JobFailurePolicy FailurePolicy { get; init; }

    /// <summary>
    /// The trigger modes that the job supports.
    /// </summary>
    public IReadOnlyList<JobTriggerMode> SupportedTriggerModes { get; init; } = [];

    /// <summary>
    /// The trigger modes that a requester can create for themselves.
    /// </summary>
    public IReadOnlyList<JobTriggerMode> RequesterTriggerModes { get; init; } = [];

    /// <summary>
    /// The default cron schedule of the job.
    /// </summary>
    public string? DefaultSchedule { get; init; }

    /// <summary>
    /// The concurrency scope of the executions.
    /// </summary>
    public ConcurrencyScope ConcurrencyScope { get; init; } = ConcurrencyScope.PerType;

    /// <summary>
    /// The parameter names that form the lock partition.
    /// </summary>
    public IReadOnlyList<string> LockPartitionFields { get; init; } = [];

    /// <summary>
    /// The lock partition fields whose values are GUIDs rather than arbitrary text.
    /// </summary>
    public IReadOnlyList<string> GuidLockPartitionFields { get; set; } = [];

    /// <summary>
    /// The event context keys that form the lock partition.
    /// </summary>
    public IReadOnlyDictionary<string, string> LockPartitionEventContextKeys { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// The boolean parameters that a queued execution sets to true when an enqueue that sets them to true coalesces
    /// onto it.
    /// </summary>
    public IReadOnlyList<string> CoalesceTrueWinsFields { get; set; } = [];

    /// <summary>
    /// The parameters that coalescing removes to widen an execution.
    /// </summary>
    public IReadOnlyList<string> CoalesceWideningFields { get; set; } = [];

    /// <summary>
    /// The parameters that the job accepts.
    /// </summary>
    public IReadOnlyList<JobParameter> Parameters { get; init; } = [];
}
