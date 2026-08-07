using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="Download"/>.
/// </summary>
public sealed record DownloadPatch
{
    /// <summary>
    /// The id of the download to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the download status.
    /// </summary>
    public ScalarPatch<DownloadStatus>? Status { get; init; }

    /// <summary>
    /// The patch for the reason behind the status.
    /// </summary>
    public ScalarPatch<string?>? StatusReason { get; init; }

    /// <summary>
    /// The patch for the output path.
    /// </summary>
    public ScalarPatch<string?>? OutputPath { get; init; }

    /// <summary>
    /// The patch for the completion time.
    /// </summary>
    public ScalarPatch<DateTime?>? DateCompleted { get; init; }

    /// <summary>
    /// The patch for the completion progress.
    /// </summary>
    public ScalarPatch<decimal?>? Progress { get; init; }

    /// <summary>
    /// The patch for the time of the last progress report.
    /// </summary>
    public ScalarPatch<DateTime?>? LastProgressAt { get; init; }

    /// <summary>
    /// The patch for the failure reason.
    /// </summary>
    public ScalarPatch<DownloadFailureReason?>? FailureReason { get; init; }

    /// <summary>
    /// The patch for the time of the failure.
    /// </summary>
    public ScalarPatch<DateTime?>? FailedAt { get; init; }

    /// <summary>
    /// The patch for the time the client removed the download.
    /// </summary>
    public ScalarPatch<DateTime?>? ClientRemovedAt { get; init; }

    /// <summary>
    /// The patch for whether the client still holds the download.
    /// </summary>
    public ScalarPatch<bool>? HeldByPolicy { get; init; }
}
