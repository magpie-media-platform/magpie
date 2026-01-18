namespace Magpie.Models.Filters;

/// <summary>
/// Operators for field filters.
/// </summary>
public enum FieldOperator
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
    /// Field has no value.
    /// </summary>
    IsNull,

    /// <summary>
    /// Field has a value.
    /// </summary>
    IsNotNull,

    /// <summary>
    /// Greater than.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Greater than or equal.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Less than.
    /// </summary>
    LessThan,

    /// <summary>
    /// Less than or equal.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Value is within a range.
    /// </summary>
    Between,

    /// <summary>
    /// Value is outside a range.
    /// </summary>
    NotBetween,

    /// <summary>
    /// Value is one of a set.
    /// </summary>
    In,

    /// <summary>
    /// Value is not one of a set.
    /// </summary>
    NotIn,

    /// <summary>
    /// String contains substring.
    /// </summary>
    ContainsString,

    /// <summary>
    /// String starts with prefix.
    /// </summary>
    StartsWithString,

    /// <summary>
    /// String ends with suffix.
    /// </summary>
    EndsWithString,

    /// <summary>
    /// String does not contain substring.
    /// </summary>
    NotContainsString,

    /// <summary>
    /// String does not start with prefix.
    /// </summary>
    NotStartsWithString,

    /// <summary>
    /// String does not end with suffix.
    /// </summary>
    NotEndsWithString,
}
