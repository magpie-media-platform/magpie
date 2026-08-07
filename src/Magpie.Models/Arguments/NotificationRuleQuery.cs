using System;
using System.Collections.Generic;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for notification rules.
/// </summary>
public sealed record NotificationRuleQuery
{
    /// <summary>
    /// The ids of the owners whose rules to match, or <c>null</c> to not constrain by owner.
    /// </summary>
    public IReadOnlyList<Guid>? OwnerUserIds { get; init; }

    /// <summary>
    /// The enabled state to match.
    /// </summary>
    public bool? Enabled { get; init; }
}
