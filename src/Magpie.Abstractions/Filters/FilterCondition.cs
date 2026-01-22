namespace Magpie.Abstractions.Filters;

/// <summary>
/// Base type for filter conditions in a predicate tree.
/// </summary>
/// <remarks>
/// Filter conditions form a composable tree structure for matching content. Leaf nodes
/// are concrete conditions (rank conditions, relationship conditions) while branch nodes
/// are composite conditions that combine children with logical operators.
/// <para>
/// Filters are serialized to JSON for storage and can be compiled to database queries
/// at runtime.
/// </para>
/// </remarks>
public abstract class FilterCondition
{
}
