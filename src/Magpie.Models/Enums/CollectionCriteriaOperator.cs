namespace Magpie.Models.Enums;

/// <summary>
/// Operator for collection criteria
/// </summary>
public enum CollectionCriteriaOperator
{
    /// <summary>
    /// Matches when at least one item in the collection matches the criteria.
    /// </summary>
    Some = 0,

    /// <summary>
    /// Matches when none of the items in the collection match the criteria.
    /// </summary>
    None,
}
