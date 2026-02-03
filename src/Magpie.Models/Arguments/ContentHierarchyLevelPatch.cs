using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ContentHierarchyItem"/>.
/// </summary>
public sealed record ContentHierarchyItemPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="ContentHierarchyItem.RootFolderId"/> patch
    /// </summary>
    public ScalarPatch<Guid?>? RootFolderId { get; init; }

    /// <summary>
    /// The <see cref="ContentHierarchyItem.Monitoring"/> patch
    /// </summary>
    public ScalarPatch<MonitoringIntent>? Monitoring { get; init; }

    /// <summary>
    /// The <see cref="ContentHierarchyItem.Path"/> patch
    /// </summary>
    public ScalarPatch<string>? Path { get; init; }

    /// <summary>
    /// The <see cref="ContentHierarchyItem.LanguagePreferences"/> patch
    /// </summary>
    public ListPatch<string, string>? LanguagePreferences { get; init; }
}
