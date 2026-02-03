using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Query arguments for <see cref="Download"/>.
/// </summary>
public record DownloadQuery
{
    /// <summary>
    /// The criteria for <see cref="Download.ReleaseId"/>.
    /// </summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>
    /// The criteria for <see cref="Download.DownloadClientId"/>.
    /// </summary>
    public Guid? DownloadClientId { get; init; }

    /// <summary>
    /// The criteria for <see cref="Download.Status"/>.
    /// </summary>
    public DownloadStatus? Status { get; init; }

    /// <summary>
    /// The criteria for <see cref="Download.DateCompleted"/>.
    /// </summary>
    public Range<DateTime>? DateCompleted { get; init; }
}
