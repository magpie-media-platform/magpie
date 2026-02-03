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
    public CriteriaSet<PropertyCriterion<string>> MimeType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Url"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Url { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Source"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Title"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Title { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.ReleaseDate"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> ReleaseDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Preview.Language"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();
}
