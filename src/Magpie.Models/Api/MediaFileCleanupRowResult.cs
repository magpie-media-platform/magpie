using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The outcome of one approved media file cleanup row.
/// </summary>
public sealed record MediaFileCleanupRowResult
{
    /// <summary>
    /// The id of the media file.
    /// </summary>
    public Guid MediaFileId { get; set; }

    /// <summary>
    /// The approved action.
    /// </summary>
    public MediaFileCleanupAction Action { get; set; }

    /// <summary>
    /// Whether the action ran.
    /// </summary>
    public bool Applied { get; set; }

    /// <summary>
    /// Why the action did not run, or <see langword="null"/> when it ran.
    /// </summary>
    public string? Message { get; set; }
}
