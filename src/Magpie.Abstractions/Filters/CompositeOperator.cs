namespace Magpie.Abstractions.Filters;

/// <summary>
/// Logical operators for combining filter conditions.
/// </summary>
public enum CompositeOperator
{
    /// <summary>
    /// All child conditions must match.
    /// </summary>
    And,

    /// <summary>
    /// At least one child condition must match.
    /// </summary>
    Or,

    /// <summary>
    /// The child condition must not match. Expects exactly one child condition.
    /// </summary>
    Not
}
