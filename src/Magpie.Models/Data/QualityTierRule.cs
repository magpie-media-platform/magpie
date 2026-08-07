using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// An accept or exclude rule that gates release eligibility.
/// </summary>
public record QualityTierRule
{
    /// <summary>
    /// The unique id of the quality tier rule.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the rule to show.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether the rule accepts or excludes a release, from <see cref="RuleKind"/>.
    /// </summary>
    public RuleKind Kind { get; set; }

    /// <summary>
    /// The criteria that a release must match for the rule.
    /// </summary>
    public required QualityTierCriteria Criteria { get; set; }
}
