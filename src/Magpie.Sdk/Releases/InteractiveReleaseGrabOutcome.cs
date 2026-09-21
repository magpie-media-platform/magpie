using System;

namespace Magpie.Sdk.Releases;

/// <summary>
/// The outcome of an interactive release grab.
/// </summary>
/// <param name="Status">The status of the grab</param>
/// <param name="ExecutionId">The id of the queued acquisition execution, or null</param>
/// <param name="Detail">The problem detail for a grab that failed</param>
public sealed record InteractiveReleaseGrabOutcome(
    InteractiveReleaseGrabResult Status,
    Guid? ExecutionId,
    string? Detail)
{
    /// <summary>
    /// The status of the grab.
    /// </summary>
    public InteractiveReleaseGrabResult Status { get; set; } = Status;

    /// <summary>
    /// The id of the queued acquisition execution, or null.
    /// </summary>
    public Guid? ExecutionId { get; set; } = ExecutionId;

    /// <summary>
    /// The problem detail for a failed grab.
    /// </summary>
    public string? Detail { get; set; } = Detail;
}
