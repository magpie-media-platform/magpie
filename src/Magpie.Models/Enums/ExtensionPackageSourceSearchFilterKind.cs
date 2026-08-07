namespace Magpie.Models.Enums;

/// <summary>
/// The discovery filter mode of an extension package source.
/// </summary>
public enum ExtensionPackageSourceSearchFilterKind
{
    /// <summary>
    /// Search results must have a given NuGet tag.
    /// </summary>
    Tag = 0,

    /// <summary>
    /// Search results must have a given package id prefix.
    /// </summary>
    Prefix = 1,

    /// <summary>
    /// The source adds no discovery filter.
    /// </summary>
    None = 2
}
