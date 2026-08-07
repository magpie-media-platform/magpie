namespace Magpie.Models.Enums;

/// <summary>
/// A comparison operator for a single criteria field value.
/// </summary>
public enum CriteriaValueOperator
{
    /// <summary>
    /// The field value is equal to the given value.
    /// </summary>
    Equals = 0,

    /// <summary>
    /// The field value is not equal to the given value.
    /// </summary>
    NotEquals,

    /// <summary>
    /// The field has no value.
    /// </summary>
    IsNull,

    /// <summary>
    /// The field has a value.
    /// </summary>
    IsNotNull,

    /// <summary>
    /// The field value is more than the given value.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// The field value is more than or equal to the given value.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// The field value is less than the given value.
    /// </summary>
    LessThan,

    /// <summary>
    /// The field value is less than or equal to the given value.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// The field value is one of a given set.
    /// </summary>
    In,

    /// <summary>
    /// The field value is not one of a given set.
    /// </summary>
    NotIn,

    /// <summary>
    /// The field value contains the given substring.
    /// </summary>
    ContainsString,

    /// <summary>
    /// The field value does not contain the given substring.
    /// </summary>
    NotContainsString,

    /// <summary>
    /// The field value starts with the given prefix.
    /// </summary>
    StartsWithString,

    /// <summary>
    /// The field value does not start with the given prefix.
    /// </summary>
    NotStartsWithString,

    /// <summary>
    /// The field value ends with the given suffix.
    /// </summary>
    EndsWithString,

    /// <summary>
    /// The field value does not end with the given suffix.
    /// </summary>
    NotEndsWithString,

    /// <summary>
    /// The field value matches a .NET regular expression pattern.
    /// </summary>
    Regex,

    /// <summary>
    /// The field value contains the given value as a whole word, not as a bare substring.
    /// </summary>
    ContainsWord,
}
