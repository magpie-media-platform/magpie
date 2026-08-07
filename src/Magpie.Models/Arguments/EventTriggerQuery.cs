using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for event triggers.
/// </summary>
public sealed record EventTriggerQuery
{
    /// <summary>
    /// The id of the extension to match.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The enabled state to match.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// The id of the creating user to match.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }
}
