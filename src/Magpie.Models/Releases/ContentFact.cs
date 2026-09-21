using System.Collections.Generic;

namespace Magpie.Models.Releases;

/// <summary>
/// One search fact about a content item, as free text, as facets, or as both.
/// </summary>
public sealed record ContentFact
{
    /// <summary>
    /// The free text value of the fact.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// The structured facets of the fact, keyed by search parameter name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Facets { get; set; } = new Dictionary<string, string>();
}
