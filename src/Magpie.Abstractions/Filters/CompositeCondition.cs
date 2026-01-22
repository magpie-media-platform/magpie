using System.Collections.ObjectModel;

namespace Magpie.Abstractions.Filters;

/// <summary>
/// A filter condition that combines child conditions with a logical operator.
/// </summary>
/// <remarks>
/// Composite conditions enable building complex filter expressions by combining
/// simpler conditions with AND, OR, or NOT operators. The tree structure allows
/// for arbitrary nesting of logical operations.
/// </remarks>
public class CompositeCondition : FilterCondition
{
    /// <summary>
    /// The logical operator used to combine child conditions.
    /// </summary>
    public CompositeOperator Operator { get; set; }

    /// <summary>
    /// The child conditions to combine.
    /// </summary>
    public Collection<FilterCondition> Conditions { get; } = new Collection<FilterCondition>();
}
