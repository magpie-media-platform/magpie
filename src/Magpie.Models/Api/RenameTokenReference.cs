using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The registered rename tokens and a preview of a rename mask.
/// </summary>
public sealed record RenameTokenReference
{
    /// <summary>
    /// The registered rename tokens.
    /// </summary>
    public IReadOnlyList<RenameTokenInfo> Tokens { get; init; } = [];

    /// <summary>
    /// The preview of the requested mask, or null when no mask is available.
    /// </summary>
    public RenameTokenPreview? Preview { get; init; }
}
