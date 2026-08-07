using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for subjects.
/// </summary>
public sealed record SubjectQuery
{
    /// <summary>
    /// The parsed filter expression to match, or <c>null</c> to not constrain by expression.
    /// </summary>
    public SubjectFilterCriteria? Criteria { get; init; }

    /// <summary>
    /// The kind slug to match, or <c>null</c> to not constrain by kind.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// The kind slugs to match, or <c>null</c> to not constrain by kind.
    /// </summary>
    public IReadOnlyCollection<string>? Kinds { get; init; }

    /// <summary>
    /// The latest last-refresh time to match, including subjects that have no last-refresh time.
    /// </summary>
    public DateTime? LastRefreshedBefore { get; init; }

    /// <summary>
    /// Whether subjects must have external ids, or <c>null</c> to not constrain by external ids.
    /// </summary>
    public bool? HasExternalIds { get; init; }

    /// <summary>
    /// The external id to match, or <c>null</c> to not constrain by external id.
    /// </summary>
    public ExternalIdCriteria? ExternalId { get; init; }

    /// <summary>
    /// The <see cref="Tag"/> name that a subject must carry, or <c>null</c> to not constrain by tag.
    /// </summary>
    public string? Tag { get; init; }

    /// <summary>
    /// The content hierarchy item id whose related subjects to match, or <c>null</c> for none.
    /// </summary>
    public Guid? RelatedContentHierarchyItemId { get; init; }

    /// <summary>
    /// Whether subjects must have content relationships, or <c>null</c> to not constrain by content relationships.
    /// </summary>
    public bool? HasContentRelationships { get; init; }

    /// <summary>
    /// Whether subjects must have user contributions, or <c>null</c> to not constrain by user contributions.
    /// </summary>
    public bool? HasUserContributions { get; init; }

    /// <summary>
    /// Whether subjects must have tags, or <c>null</c> to not constrain by tags.
    /// </summary>
    public bool? HasTags { get; init; }

    /// <summary>
    /// Whether subjects must have been created by a non-system user, or <c>null</c> to not constrain by creator.
    /// </summary>
    public bool? IsUserCreated { get; init; }
}
