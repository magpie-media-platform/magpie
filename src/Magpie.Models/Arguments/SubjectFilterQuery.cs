using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for saved subject filters.
/// </summary>
public sealed record SubjectFilterQuery
{
    /// <summary>
    /// The exact filter name to match, or <c>null</c> to not constrain by name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The <see cref="Data.Tag"/> name that a filter must carry, or <c>null</c> to not constrain by tag.
    /// </summary>
    public string? Tag { get; init; }

    /// <summary>
    /// The id of the owner whose filters to match, or <c>null</c> to not constrain by owner.
    /// </summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>
    /// Whether to match only shared filters, or <c>null</c> to not constrain by sharing.
    /// </summary>
    public bool? SharedOnly { get; init; }
}
