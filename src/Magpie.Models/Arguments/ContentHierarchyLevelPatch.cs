using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ContentHierarchyItem"/>.
/// </summary>
/// <param name="Id">The <see cref="ContentHierarchyItem.Id"/> of the item to patch.</param>
/// <param name="RootFolderId">The <see cref="ContentHierarchyItem.RootFolderId"/> patch</param>
/// <param name="Monitoring">The <see cref="ContentHierarchyItem.Monitoring"/> patch</param>
/// <param name="Path">The <see cref="ContentHierarchyItem.Path"/> patch</param>
/// <param name="LanguagePreferences">The <see cref="ContentHierarchyItem.LanguagePreferences"/> patch</param>
/// <param name="Tags">The <see cref="Tag"/>s patch</param>
public sealed record ContentHierarchyItemPatch(
    Guid Id,
    ScalarPatch<Guid?>? RootFolderId = null,
    ScalarPatch<MonitoringIntent>? Monitoring = null,
    ScalarPatch<string>? Path = null,
    ListPatch<string, string>? LanguagePreferences = null,
    SetPatch<string, string>? Tags = null) : TagsPatch(Id, Tags);
