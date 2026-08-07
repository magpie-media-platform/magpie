using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// A reference to a related subject from a provider metadata search candidate.
/// </summary>
public sealed record ProviderMetadataSearchSubjectReference
{
    /// <summary>
    /// The kind slug of the subject.
    /// </summary>
    public required string SubjectKind { get; init; }

    /// <summary>
    /// The display name of the subject.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The kind of the relationship to the subject.
    /// </summary>
    public string? RelationshipKind { get; init; }

    /// <summary>
    /// The qualifier of the relationship to the subject.
    /// </summary>
    public string? RelationshipQualifier { get; init; }

    /// <summary>
    /// The external identifiers of the subject, keyed by source.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExternalIds { get; init; } = new Dictionary<string, string>();
}
