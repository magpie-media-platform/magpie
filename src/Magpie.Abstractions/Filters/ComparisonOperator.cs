namespace Magpie.Abstractions.Filters;

/// <summary>
/// Comparison operators for filter conditions.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>
    /// The value equals the specified value.
    /// </summary>
    Equals,

    /// <summary>
    /// The value does not equal the specified value.
    /// </summary>
    NotEquals,

    /// <summary>
    /// The value contains the specified substring.
    /// </summary>
    Contains,

    /// <summary>
    /// The value starts with the specified prefix.
    /// </summary>
    StartsWith,

    /// <summary>
    /// The value ends with the specified suffix.
    /// </summary>
    EndsWith,

    /// <summary>
    /// The value is greater than the specified value.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// The value is less than the specified value.
    /// </summary>
    LessThan,

    /// <summary>
    /// The value is greater than or equal to the specified value.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// The value is less than or equal to the specified value.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// The value is in the specified collection.
    /// </summary>
    In,

    /// <summary>
    /// The value is not in the specified collection.
    /// </summary>
    NotIn,

    /// <summary>
    /// The value matches the specified regular expression pattern.
    /// </summary>
    Matches
}
