using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for an <see cref="Image"/>.
/// </summary>
public sealed record ImageCriteria
{
    /// <summary>
    /// Criteria for the type of the image.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Type { get; set; } = new();

    /// <summary>
    /// Criteria for the MIME type of the image.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> MimeType { get; set; } = new();

    /// <summary>
    /// Criteria for the URL of the image.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Url { get; set; } = new();

    /// <summary>
    /// Criteria for the name of the source.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; set; } = new();

    /// <summary>
    /// Criteria for the language of text in the image.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; set; } = new();

    /// <summary>
    /// Criteria for the image width in pixels.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> Width { get; set; } = new();

    /// <summary>
    /// Criteria for the image height in pixels.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> Height { get; set; } = new();
}
