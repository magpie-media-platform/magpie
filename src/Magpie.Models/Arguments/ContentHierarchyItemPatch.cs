using System;
using System.Text.Json.Serialization;

using Magpie.Models.Data;
using Magpie.Models.Enums;
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
    public ScalarPatch<MonitoringMode?>? Monitoring { get; init; }

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

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.Name"/>.
    /// </summary>
    [JsonIgnore]
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.Slug"/>.
    /// </summary>
    [JsonIgnore]
    public ScalarPatch<string>? Slug { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.Settings"/>.
    /// </summary>
    [JsonIgnore]
    public DictionaryPatch<string, string, string>? Settings { get; init; }

    /// <summary>
    /// The patch for <see cref="ContentHierarchyItem.DateLastRefreshed"/>.
    /// </summary>
    [JsonIgnore]
    public ScalarPatch<DateTime?>? DateLastRefreshed { get; init; }
}
