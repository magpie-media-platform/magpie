using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A group of related configuration settings.
/// </summary>
public record SettingsGroup
{
    /// <summary>
    /// The settings group id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the group, unique per extension.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The extension this settings group belongs to.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// Settings within this group.
    /// </summary>
    public ICollection<Setting> Settings { get; } = [];
}
