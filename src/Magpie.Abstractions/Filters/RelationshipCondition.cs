namespace Magpie.Abstractions.Filters;

/// <summary>
/// A filter condition that matches based on related subjects.
/// </summary>
/// <remarks>
/// Relationship conditions filter content based on associated metadata entities (subjects).
/// Since relationships are many-to-many, this condition checks for the existence of at
/// least one related subject matching the criteria.
/// <para>
/// For example, "has Actor where LastName = MacMuggly" matches any content hierarchy item
/// that has a relationship to a Person subject with the Actor relation whose LastName
/// property equals "MacMuggly".
/// </para>
/// </remarks>
public class RelationshipCondition : FilterCondition
{
    /// <summary>
    /// The type of subject to match (Person, Company, Franchise, etc.).
    /// </summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>
    /// The relation type to filter by, or null to match any relation.
    /// </summary>
    /// <remarks>
    /// When specified, only relationships with this relation type are considered (e.g.,
    /// Actor, Director, Studio). When null, any relation is accepted.
    /// </remarks>
    public string? Relation { get; set; }

    /// <summary>
    /// The property on the subject to compare (Name, LastName, Founded, etc.).
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
