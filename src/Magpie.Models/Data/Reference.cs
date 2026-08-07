using System;

namespace Magpie.Models.Data;

/// <summary>
/// A link to an external page for content.
/// </summary>
public record Reference
{
    /// <summary>
    /// The unique id of the reference.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the contribution that supplied the link.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The position in display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of link, such as homepage or review.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The name of the source to credit for the link.
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// The URL of the external page.
    /// </summary>
    public required Uri Url { get; set; }

    /// <summary>
    /// The text to show for the link.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The language of the external page, as a language code.
    /// </summary>
    public string? Language { get; set; }
}
