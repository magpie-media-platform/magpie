using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The filter criteria for a query over content hierarchy items.
/// </summary>
public sealed record ContentQuery
{
    /// <summary>
    /// The parsed filter expression, or <see langword="null"/> for no expression filter.
    /// </summary>
    public ContentFilterCriteria? Criteria { get; set; }

    /// <summary>
    /// The content type slug to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// The content level slug to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// The id of the parent item to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Whether the result holds only root-level items, or <see langword="null"/> for no filter.
    /// </summary>
    public bool? TopLevelOnly { get; set; }

    /// <summary>
    /// The id of the root folder to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public Guid? RootFolderId { get; set; }

    /// <summary>
    /// The effective monitoring state to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public bool? Monitoring { get; set; }

    /// <summary>
    /// The fulfillment status to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public FulfillmentStatus? Fulfillment { get; set; }

    /// <summary>
    /// The title search term to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// The tag name to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public string? Tag { get; set; }
}
