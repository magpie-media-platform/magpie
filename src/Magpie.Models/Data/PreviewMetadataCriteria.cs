using System;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The filter criteria for a <see cref="Preview"/>.
/// </summary>
public sealed record PreviewMetadataCriteria
{
    /// <summary>
    /// The criteria on the MIME type of the preview.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> MimeType { get; init; } = new();

    /// <summary>
    /// The criteria on the URL of the preview.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Url { get; init; } = new();

    /// <summary>
    /// The criteria on the name of the upstream source.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; init; } = new();

    /// <summary>
    /// The criteria on the title of the preview.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Title { get; init; } = new();

    /// <summary>
    /// The criteria on the date when the preview became public.
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> ReleaseDate { get; init; } = new();

    /// <summary>
    /// The criteria on the language code of the preview.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();
}
