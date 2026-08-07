namespace Magpie.Sdk.Content;

/// <summary>
/// The custom metadata field keys for a relationship between a candidate and a subject.
/// </summary>
public static class RelationshipFieldKeys
{
    /// <summary>
    /// The field key of the related subject id.
    /// </summary>
    public const string Subject = "relationship.subject";

    /// <summary>
    /// The field key prefix of the related subject id for one subject kind.
    /// </summary>
    public const string SubjectKindPrefix = "relationship.subject.";

    /// <summary>
    /// The field key of the subject kind.
    /// </summary>
    public const string Kind = "relationship.kind";

    /// <summary>
    /// The field key of the relationship type.
    /// </summary>
    public const string Type = "relationship.type";

    /// <summary>
    /// The field key of the external id of the related subject.
    /// </summary>
    public const string ExternalId = "relationship.externalId";

    /// <summary>
    /// The field key of the source of the external id.
    /// </summary>
    public const string ExternalIdSource = "relationship.externalIdSource";
}
