using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Query arguments for <see cref="Download"/>.
/// </summary>
/// <param name="ReleaseId">The criteria for <see cref="Download.ReleaseId"/>.</param>
/// <param name="DownloadClientId">The criteria for <see cref="Download.DownloadClientId"/>.</param>
/// <param name="Status">The criteria for <see cref="Download.Status"/>.</param>
/// <param name="DateCompleted">The criteria for <see cref="Download.DateCompleted"/>.</param>
public record DownloadQuery(
    Guid? ReleaseId = null,
    Guid? DownloadClientId = null,
    DownloadStatus? Status = null,
    Range<DateTime>? DateCompleted = null);
