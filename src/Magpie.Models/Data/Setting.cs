using System;

namespace Magpie.Models.Data;

/// <summary>
/// A configuration setting.
/// </summary>
public record Setting
{
    /// <summary>
    /// The setting id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The settings group this setting belongs to.
    /// </summary>
    public Guid SettingsGroupId { get; set; }

    /// <summary>
    /// The setting key.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The setting value.
    /// </summary>
    public required string Value { get; set; }

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
}
