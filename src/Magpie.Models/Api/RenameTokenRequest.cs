using System;

namespace Magpie.Models.Api;

/// <summary>
/// Requests the rename token reference and a preview of a rename mask.
/// </summary>
public sealed record RenameTokenRequest
{
    /// <summary>
    /// The content type whose hierarchy tokens to list, when no content is selected.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// The mask to preview, or null to preview the configured mask of the selected content.
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// The selected content that owns the file, or null to use example values.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; set; }

    /// <summary>
    /// The selected file of the selected content.
    /// </summary>
    public Guid? MediaFileId { get; set; }
}
