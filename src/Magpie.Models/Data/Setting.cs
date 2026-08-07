using System;

namespace Magpie.Models.Data;

/// <summary>
/// One named value in a settings group.
/// </summary>
public record Setting
{
    /// <summary>
    /// The unique id of the setting.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the group that holds the setting.
    /// </summary>
    public Guid SettingsGroupId { get; set; }

    /// <summary>
    /// The name of the setting within the group.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The value of the setting, as text.
    /// </summary>
    public required string Value { get; set; }

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
