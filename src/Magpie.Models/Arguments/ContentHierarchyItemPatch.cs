using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="ContentHierarchyItem"/>.
/// </summary>
public sealed record ContentHierarchyItemPatch : TagsPatch
{
    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.QualityTierListId"/>.
    /// </summary>
    public ScalarPatch<Guid?>? QualityTierListId { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.RootFolderId"/>.
    /// </summary>
    public ScalarPatch<Guid?>? RootFolderId { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.Monitoring"/>.
    /// </summary>
    public ScalarPatch<bool?>? Monitoring { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.Path"/>.
    /// </summary>
    public ScalarPatch<string?>? Path { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.LanguagePreferences"/>.
    /// </summary>
    public ListPatch<string, string>? LanguagePreferences { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.PendingMetadataRefresh"/>.
    /// </summary>
    public ScalarPatch<bool>? PendingMetadataRefresh { get; init; }
}
