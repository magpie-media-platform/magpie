using System.Collections.Generic;

namespace Magpie.Models.UI;

/// <summary>
/// Combined UI manifest containing all assets and extensions.
/// </summary>
public sealed record UIManifest
{
    /// <summary>
    /// All registered asset definitions.
    /// </summary>
    public required IReadOnlyList<Asset> Assets { get; init; }

    /// <summary>
    /// All registered UI extension definitions.
    /// </summary>
    public required IReadOnlyList<UIExtension> Extensions { get; init; }
}
