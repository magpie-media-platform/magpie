using System;

namespace Magpie.Core.Config;

/// <summary>
/// The job runtime options for a Magpie host.
/// </summary>
public sealed class JobRuntimeOptions
{
    /// <summary>
    /// The configuration section name for these options.
    /// </summary>
    public const string SectionName = "Magpie:Jobs";

    /// <summary>
    /// The maximum number of concurrent job executions.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 30;

    /// <summary>
    /// The heartbeat age after which a running job becomes stale.
    /// </summary>
    public TimeSpan RunningStaleCutoff { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The interval between stale-job cleanup passes.
    /// </summary>
    public TimeSpan StaleJobCleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The interval between passes that reclaim running executions past <c>TimeoutAt</c>.
    /// </summary>
    public TimeSpan TimedOutJobReapInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The per-level MLFQ quantum overrides, indexed by level.
    /// </summary>
    public TimeSpan?[] MlfqQuanta { get; set; } = new TimeSpan?[4];
}
