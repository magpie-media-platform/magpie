using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A group of related settings.
/// </summary>
public record SettingsGroup
{
    /// <summary>
    /// The unique id of the settings group.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the group, unique per extension.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The id of the extension that owns the group.
    /// </summary>
    public Guid? ExtensionId { get; set; }

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

    /// <summary>
    /// The settings in the group.
    /// </summary>
    public ICollection<Setting> Settings { get; } = [];
}
