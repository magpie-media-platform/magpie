using System;

using Magpie.Models.Enums;

namespace Magpie.Models.UI;

/// <summary>
/// A UI asset provided by an extension.
/// </summary>
public sealed record Asset
{
    /// <summary>
    /// The unique identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The id of the extension that provides this asset.
    /// </summary>
    public required Guid ExtensionId { get; init; }

    /// <summary>
    /// The asset display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The type of asset.
    /// </summary>
    public AssetKind Kind { get; init; }

    /// <summary>
    /// Remote URL for the asset. Mutually exclusive with RelativePath.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Path for embedded assets. Mutually exclusive with Url.
    /// </summary>
    public string? RelativePath { get; init; }

    /// <summary>
    /// For Link assets: the rel attribute value.
    /// </summary>
    public string? LinkRel { get; init; }

    /// <summary>
    /// SRI hash for remote assets.
    /// </summary>
    public string? Integrity { get; init; }

    /// <summary>
    /// Asset loading priority.
    /// </summary>
    public int Priority { get; init; }
}
