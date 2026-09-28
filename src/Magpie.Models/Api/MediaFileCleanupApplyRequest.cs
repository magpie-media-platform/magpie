using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The approved rows of a media file cleanup plan.
/// </summary>
public sealed record MediaFileCleanupApplyRequest
{
    /// <summary>
    /// The root item that the plan was made for, or <see langword="null"/> for the whole library.
    /// </summary>
    public Guid? RootId { get; set; }

    /// <summary>
    /// The approved rows. A row applies only when it still matches the current plan.
    /// </summary>
    public List<MediaFileCleanupRow> Rows { get; set; } = [];
}
