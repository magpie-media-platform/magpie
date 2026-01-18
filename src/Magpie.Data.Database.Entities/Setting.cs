using System;

namespace Magpie.Data.Database.Entities;

public class Setting
{
    public required Guid Id { get; set; }
    public required Guid SettingsGroupId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public required SettingsGroup SettingsGroup { get; set; }
}
