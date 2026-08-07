using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A per-user routing rule from events to a notification provider.
/// </summary>
public record NotificationRule
{
    /// <summary>
    /// The unique id of the notification rule.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the rule to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The id of the user who owns the rule.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// The <see cref="EventFilter"/> that selects the events for the rule.
    /// </summary>
    public EventFilter EventFilter { get; set; } = new();

    /// <summary>
    /// The name of the provider type that sends the notification.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The provider settings, keyed by name.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// Whether the rule is in use.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
