using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query criteria for tags.
/// </summary>
public sealed record TagQuery
{
    /// <summary>
    /// The name prefix to match, or <c>null</c> for all names.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// The id of the tagged entity, or <c>null</c> for all entities.
    /// </summary>
    public Guid? EntityId { get; init; }
}
