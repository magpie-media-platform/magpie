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
    public required string RelationshipKind { get; set; }

    /// <summary>
    /// The qualifier of the relationship.
    /// </summary>
    public string? RelationshipQualifier { get; set; }

    /// <summary>
    /// The position of the relationship in the display order.
    /// </summary>
    public int RelationshipOrdinal { get; set; }

    /// <summary>
    /// The kind slug of the related subject.
    /// </summary>
    public required string SubjectKind { get; set; }

    /// <summary>
    /// The name of the related subject.
    /// </summary>
    public required string SubjectName { get; set; }

    /// <summary>
    /// The external identifiers of the related subject, keyed by source.
    /// </summary>
    public IReadOnlyDictionary<string, string> SubjectExternalIds { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// The first metadata contribution for the related subject, when any.
    /// </summary>
    public MetadataContribution? InitialSubjectContribution { get; set; }
}
