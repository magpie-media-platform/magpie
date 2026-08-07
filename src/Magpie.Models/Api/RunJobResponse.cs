using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The response to a job run request.
/// </summary>
public sealed record RunJobResponse
{
    /// <summary>
    /// The id of the job execution.
    /// </summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>
    /// The status of the job execution, when a status is available.
    /// </summary>
    public JobExecutionStatus? Status { get; init; }

    /// <summary>
    /// The message that describes the status of the job execution, when a message is available.
    /// </summary>
    public string? StatusMessage { get; init; }
}
