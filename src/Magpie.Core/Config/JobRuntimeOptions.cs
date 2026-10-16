using System;
using System.Collections.Generic;

namespace Magpie.Core.Config;

/// <summary>
/// The job runtime options for a Magpie host.
/// </summary>
public sealed class JobRuntimeOptions
{
    /// <summary>
    /// The configuration section name for these options.
    /// </summary>
    public const string SectionName = "Jobs";

    /// <summary>
    /// The number of workers in the job pool.
    /// </summary>
    public int PoolSize { get; set; } = 50;

    /// <summary>
    /// The heartbeat age after which a running job becomes stale.
    /// </summary>
    public TimeSpan RunningStaleCutoff { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The interval between stale-job cleanup passes.
    /// </summary>
    public TimeSpan StaleJobCleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The maximum interval between stale-job cleanup passes on clustered nodes.
    /// </summary>
    public TimeSpan ClusteredStaleJobCleanupInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The time that the progress reporter waits to flush progress.
    /// </summary>
    public TimeSpan ProgressFlushTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The per-level MLFQ quantum overrides, indexed by level.
    /// </summary>
    public TimeSpan?[] MlfqQuanta { get; set; } = new TimeSpan?[4];

    /// <summary>
    /// The job types that the node runs.
    /// </summary>
    public List<string> JobTypes { get; set; } = [];

    /// <summary>
    /// The maximum number of executions that run at once, by job type.
    /// </summary>
    public Dictionary<string, int> ConcurrencyLimits { get; set; } = [];
}
