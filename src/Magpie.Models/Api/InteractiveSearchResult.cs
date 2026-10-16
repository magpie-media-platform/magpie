using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The candidates and completion status of an interactive release search.
/// </summary>
public sealed record InteractiveSearchResult
{
    /// <summary>
    /// The candidates returned by available providers.
    /// </summary>
    public IReadOnlyList<InteractiveSearchCandidate> Candidates { get; set; } = [];

    /// <summary>
    /// True when one or more provider branches did not complete.
    /// </summary>
    public bool Partial { get; set; }

    /// <summary>
    /// The reason that the search is incomplete.
    /// </summary>
    public string? Warning { get; set; }
}
