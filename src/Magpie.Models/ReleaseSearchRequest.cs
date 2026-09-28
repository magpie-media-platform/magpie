using System;
using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// A content-type-agnostic release search request.
/// </summary>
public sealed record ReleaseSearchRequest
{
    /// <summary>
    /// Whether the search serves a caller who is waiting for its results.
    /// </summary>
    public bool IsInteractive { get; set; }

    /// <summary>
    /// The primary title to search for.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The release year or publication year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// The content-type criteria, keyed by criterion name.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Criteria { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// The indexer search parameter slug, when this is an identifier query.
    /// </summary>
    public string? IdSearchParameter { get; init; }

    /// <summary>
    /// The parameter value for the indexer, when this is an identifier query.
    /// </summary>
    public string? IdSearchValue { get; init; }

    /// <summary>
    /// The external identifier source slug that the identifier query comes from.
    /// </summary>
    public string? IdSearchSource { get; init; }

    /// <summary>
    /// The hierarchy level slug that the identifier query scopes to.
    /// </summary>
    public string? IdSearchLevelSlug { get; init; }

    /// <summary>
    /// The full source-specific identity value that the identifier query comes from.
    /// </summary>
    public string? IdSearchIdentityValue { get; init; }

    /// <summary>
    /// The id of the content hierarchy item that owns the identifier query.
    /// </summary>
    public Guid? IdSearchContentHierarchyItemId { get; init; }

    /// <summary>
    /// The category slugs to search in.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// The maximum number of results that the caller needs from this search.
    /// </summary>
    public int MaximumResults { get; init; } = 20;
}
