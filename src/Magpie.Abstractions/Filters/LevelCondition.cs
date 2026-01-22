namespace Magpie.Abstractions.Filters;

/// <summary>
/// A filter condition that matches properties on content hierarchy items.
/// </summary>
/// <remarks>
/// Level conditions filter content based on properties at a specific level in the hierarchy.
/// Since content hierarchy items have a single-parent hierarchy, a condition on a parent level
/// (e.g., Series) applies to all descendants (e.g., Episodes in that Series).
/// <para>
/// The level name specifies which level in the hierarchy to evaluate (Episode, Season, Series,
/// Movie, Album, etc.), the property specifies which attribute to compare (Name, Year,
/// Title, Number), and the operator and value define the comparison.
/// </para>
/// </remarks>
public class LevelCondition : FilterCondition
{
    /// <summary>
    /// The level name in the hierarchy to evaluate (Episode, Series, Movie, Album, etc.).
    /// </summary>
    public string LevelName { get; set; } = string.Empty;

    /// <summary>
    /// The property on the content hierarchy item to compare (Name, Year, Title, Number, etc.).
    /// </summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>
    /// The comparison operator to use.
    /// </summary>
    public ComparisonOperator Operator { get; set; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public object? Value { get; set; }
}
