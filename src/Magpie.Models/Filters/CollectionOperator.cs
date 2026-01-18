namespace Magpie.Models.Filters;

/// <summary>
/// Operators for matching against collections.
/// </summary>
public enum CollectionOperator
{
    /// <summary>
    /// Matches if any item in the collection matches the criteria.
    /// </summary>
    Any = 0,

    /// <summary>
    /// Matches if all items in the collection match the criteria.
    /// </summary>
    All,

    /// <summary>
    /// Matches if no items in the collection match the criteria.
    /// </summary>
    None
}
