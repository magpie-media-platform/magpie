namespace Magpie.Models.QualityTiers;

/// <summary>
/// Operators for quality criterion matching.
/// </summary>
public enum QualityCriterionOperator
{
    /// <summary>
    /// Exact equality.
    /// </summary>
    Equals = 0,

    /// <summary>
    /// Not equal.
    /// </summary>
    NotEquals,

    /// <summary>
    /// Value contains the pattern.
    /// </summary>
    Contains,

    /// <summary>
    /// Value does not contain the pattern.
    /// </summary>
    NotContains,

    /// <summary>
    /// Value matches a regex pattern.
    /// </summary>
    Matches,

    /// <summary>
    /// Value does not match a regex pattern.
    /// </summary>
    NotMatches
}
