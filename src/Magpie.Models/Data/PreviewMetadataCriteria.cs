using System;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Preview"/>.
/// </summary>
public sealed record PreviewMetadataCriteria
{
    /// <summary>
    /// The criteria for <see cref="Preview.Kind"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> MimeType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Url"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Url { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Source"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Source { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Title"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Title { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.ReleaseDate"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<DateTime>> ReleaseDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Language"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Language { get; init; } = new();
}
