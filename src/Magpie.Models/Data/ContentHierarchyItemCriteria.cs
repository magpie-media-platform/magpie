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
    public CriteriaSet<PropertyCriterion<Guid>> ParentId { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.RootFolderId"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<Guid>> RootFolderId { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.ContentType"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> ContentType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Level"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Level { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Slug"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Slug { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Name"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Monitoring"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<MonitoringIntent>> Monitoring { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentHierarchyItem.Path"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Path { get; init; } = new();
    /// <summary>
    /// The criteria for related <see cref="Tag"/>s.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Tags { get; init; } = new();
    /// <summary>
    /// The criteria for related metadata.
    /// </summary>
    public CriteriaSet<MetadataCriteria> Metadata { get; init; } = new();
}
