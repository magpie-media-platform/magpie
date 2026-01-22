using System;

namespace Magpie.Data.Database.Entities;

public record Setting
{
    public Guid Id { get; set; }
    public Guid SettingsGroupId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public SettingsGroup? SettingsGroup { get; set; }
}
