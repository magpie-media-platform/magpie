using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// An installed extension with its update availability.
/// </summary>
public sealed record ExtensionListItem
{
    /// <summary>
    /// The installed extension.
    /// </summary>
    public required Extension Extension { get; init; }

    /// <summary>
    /// The update availability of the extension, or <see langword="null"/> when it was not evaluated.
    /// </summary>
    public ExtensionUpdateInfo? UpdateInfo { get; init; }
}
