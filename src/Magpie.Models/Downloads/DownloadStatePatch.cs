using System;

namespace Magpie.Models.Downloads;

/// <summary>
/// Patch for updating the state of a <see cref="Download"/>.
/// </summary>
/// <param name="State">The new state.</param>
/// <param name="OutputPath">The output path, if applicable.</param>
/// <param name="FailureReason">The failure reason, if failed.</param>
/// <param name="DateCompleted">The completion date, if completed.</param>
public sealed record DownloadStatePatch(
    DownloadState State,
    string? OutputPath,
    string? FailureReason,
    DateTimeOffset? DateCompleted);
