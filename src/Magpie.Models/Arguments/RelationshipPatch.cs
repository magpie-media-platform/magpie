using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a relationship.
/// </summary>
public sealed record RelationshipPatch
{
    /// <summary>
    /// The id of the relationship to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the kind of relationship.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The patch for the qualifier of the relationship.
    /// </summary>
    public ScalarPatch<string?>? Qualifier { get; init; }

    /// <summary>
    /// The id of an existing related subject, which excludes <see cref="SubjectName"/> and <see cref="SubjectKind"/>.
    /// </summary>
    public Guid? SubjectId { get; init; }

    /// <summary>
    /// The display name of a related subject to create, which requires <see cref="SubjectKind"/>.
    /// </summary>
    public string? SubjectName { get; init; }

    /// <summary>
    /// The kind slug of a related subject to create, which requires <see cref="SubjectName"/>.
    /// </summary>
    public string? SubjectKind { get; init; }
}
