using System;

namespace Magpie.Models;

/// <summary>
/// A job execution that a worker can claim.
/// </summary>
/// <param name="Id">The id of the job execution</param>
/// <param name="JobType">The stable type identifier of the job</param>
/// <param name="LockPartition">The lock partition key, or null when the job is not partitioned</param>
public readonly record struct JobClaimCandidate(Guid Id, string JobType, string? LockPartition);
