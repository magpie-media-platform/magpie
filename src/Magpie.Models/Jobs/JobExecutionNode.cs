using System;
using System.Collections.Generic;

namespace Magpie.Models.Jobs;

/// <summary>
/// One job execution in a job execution tree.
/// </summary>
public sealed record JobExecutionNode
{
    /// <summary>
    /// The id of the job execution.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The id of the parent job execution.
    /// </summary>
    public Guid? ParentExecutionId { get; init; }

    /// <summary>
    /// The id that groups related job executions.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// The name of the job type of this execution.
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// The name of the status of this execution.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The UTC time when the execution record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The UTC time when the execution started.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// The UTC time when the execution finished.
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// The job executions that this execution started.
    /// </summary>
    public required IReadOnlyList<JobExecutionNode> Children { get; init; }
}
