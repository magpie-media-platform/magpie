using System;

namespace Magpie.Data.Database.Entities;

public record MediaServerSetting
{
    public Guid Id { get; set; }
    public Guid MediaServerId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public MediaServer? MediaServer { get; set; }
}
