using System;

using Magpie.Models.Enums;

namespace Magpie.Models.UI;

/// <summary>
/// A UI asset that an extension supplies.
/// </summary>
public sealed record Asset
{
    /// <summary>
    /// The id of this asset.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The id of the extension that owns this asset.
    /// </summary>
    public required Guid ExtensionId { get; init; }

    /// <summary>
    /// The name of this asset.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The kind of this asset: a script, a style, or a link.
    /// </summary>
    public AssetKind Kind { get; init; }

    /// <summary>
    /// The remote URL of the asset. Only one of this and <see cref="RelativePath"/> has a value.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// The path of an embedded asset. Only one of this and <see cref="Url"/> has a value.
    /// </summary>
    public string? RelativePath { get; init; }

    /// <summary>
    /// The value of the rel attribute for a link asset.
    /// </summary>
    public string? LinkRel { get; init; }

    /// <summary>
    /// The subresource integrity hash of a remote asset.
    /// </summary>
    public string? Integrity { get; init; }

    /// <summary>
    /// The load order of the asset.
    /// </summary>
    public int Priority { get; init; }
}
