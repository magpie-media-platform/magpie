using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// The definition of a relationship to a subject.
/// </summary>
public sealed record SubjectRelationshipDefinition
{
    /// <summary>
    /// The kind of the relationship.
    /// </summary>
    public required string RelationshipKind { get; init; }

    /// <summary>
    /// The qualifier of the relationship.
    /// </summary>
    public string? RelationshipQualifier { get; init; }

    /// <summary>
    /// The position of the relationship in the display order.
    /// </summary>
    public int RelationshipOrdinal { get; init; }

    /// <summary>
    /// The kind slug of the related subject.
    /// </summary>
    public required string SubjectKind { get; init; }

    /// <summary>
    /// The name of the related subject.
    /// </summary>
    public required string SubjectName { get; init; }

    /// <summary>
    /// The external identifiers of the related subject, keyed by source.
    /// </summary>
    public IReadOnlyDictionary<string, string> SubjectExternalIds { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The first metadata contribution for the related subject, when any.
    /// </summary>
    public MetadataContribution? InitialSubjectContribution { get; init; }
}
