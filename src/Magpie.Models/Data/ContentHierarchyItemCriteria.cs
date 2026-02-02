using System;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="ContentHierarchyItem"/>.
/// </summary>
public sealed record ContentHierarchyItemCriteria
{
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.ParentId"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<Guid>> ParentId { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.RootFolderId"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<Guid>> RootFolderId { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.ContentType"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> ContentType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Level"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Level { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Slug"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Slug { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Name"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Name { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Monitoring"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<MonitoringIntent>> Monitoring { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Path"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Path { get; init; } = new();
    /// <summary>
    /// The criteria for related <see cref="Tag"/>s.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Tags { get; init; } = new();
    /// <summary>
    /// The criteria for related metadata.
    /// </summary>
    public AllCriteria<MetadataCriteria> Metadata { get; init; } = new();
}
