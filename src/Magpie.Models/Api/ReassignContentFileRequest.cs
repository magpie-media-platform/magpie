using System;

namespace Magpie.Models.Api;

/// <summary>
/// A request to correct a file association within a content hierarchy.
/// </summary>
public sealed record ReassignContentFileRequest
{
    /// <summary>
    /// The selected destination in the same content hierarchy.
    /// </summary>
    public Guid TargetContentHierarchyItemId { get; set; }

    /// <summary>
    /// The file path shown when the correction was requested.
    /// </summary>
    public required string ExpectedFilePath { get; set; }
}
