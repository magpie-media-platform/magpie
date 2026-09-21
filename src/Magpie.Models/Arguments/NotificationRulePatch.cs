using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a notification rule.
/// </summary>
public sealed record NotificationRulePatch
{
    /// <summary>
    /// The id of the notification rule to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the display name of the rule.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the <see cref="EventFilter"/> that selects matching events.
    /// </summary>
    public ScalarPatch<EventFilter>? EventFilter { get; init; }

    /// <summary>
    /// The patch for the provider settings.
    /// </summary>
    public DictionaryPatch<string, string?, string?>? Settings { get; init; }

    /// <summary>
    /// The patch for whether the rule is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }
}
