using System;

using Magpie.Models.Enums;
using Magpie.Models.Paging;

namespace Magpie.Models.Arguments;

/// <summary>
/// A paged query over the merged audit-history feed.
/// </summary>
public sealed record AuditHistoryQuery
{
    /// <summary>
    /// The page number and page size.
    /// </summary>
    public required PagingParameter Paging { get; set; }

    /// <summary>
    /// The content id to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public Guid? ContentId { get; set; }

    /// <summary>
    /// The source kind to filter on, or <see langword="null"/> for both kinds.
    /// </summary>
    public AuditHistoryEntryKind? Kind { get; set; }

    /// <summary>
    /// The status to filter on, or <see langword="null"/> for no filter.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// The inclusive lower bound on the timestamp, or <see langword="null"/> for no lower bound.
    /// </summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// The inclusive upper bound on the timestamp, or <see langword="null"/> for no upper bound.
    /// </summary>
    public DateTime? To { get; set; }
}
