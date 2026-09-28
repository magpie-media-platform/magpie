using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// A file in a media file cleanup plan.
/// </summary>
public sealed record MediaFileCleanupRow
{
    /// <summary>
    /// The id of the media file.
    /// </summary>
    public Guid MediaFileId { get; set; }

    /// <summary>
    /// The path of the file.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Whether the file is a primary file of the leaf level, rather than a sidecar.
    /// </summary>
    public bool Primary { get; set; }

    /// <summary>
    /// The id of the item that the file belongs to now.
    /// </summary>
    public Guid CurrentItemId { get; set; }

    /// <summary>
    /// The names of the item that the file belongs to now and of its ancestors, from the root.
    /// </summary>
    public required string CurrentItem { get; set; }

    /// <summary>
    /// The id of the item that the binding matched, for a reassign row.
    /// </summary>
    public Guid? TargetItemId { get; set; }

    /// <summary>
    /// The names of the matched item and of its ancestors, from the root, for a reassign row.
    /// </summary>
    public string? TargetItem { get; set; }

    /// <summary>
    /// The action of the row.
    /// </summary>
    public MediaFileCleanupAction Action { get; set; }

    /// <summary>
    /// Why the plan chose the action.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// The quality tier rank of the file, where a lower rank is better, or <see langword="null"/> when the plan did
    /// not rank it.
    /// </summary>
    public int? Rank { get; set; }

    /// <summary>
    /// The id of the copy that stays, for a row that a better copy supersedes.
    /// </summary>
    public Guid? KeepMediaFileId { get; set; }

    /// <summary>
    /// The path of the copy that stays, for a row that a better copy supersedes.
    /// </summary>
    public string? KeepFilePath { get; set; }
}
