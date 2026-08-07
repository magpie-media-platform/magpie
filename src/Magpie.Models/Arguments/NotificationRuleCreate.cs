using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for a notification rule.
/// </summary>
public sealed record NotificationRuleCreate
{
    /// <summary>
    /// The display name of the rule.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The id of the user who owns the rule, or <see langword="null"/> for a global rule.
    /// </summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>
    /// The <see cref="EventFilter"/> that selects matching events.
    /// </summary>
    public EventFilter EventFilter { get; init; } = new();

    /// <summary>
    /// The notification provider type.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The provider settings.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// Whether the rule is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
