using System;

namespace Magpie.Data.Database.Entities;

public class MediaServerSetting
{
    public Guid Id { get; set; }
    public Guid MediaServerId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required MediaServer MediaServer { get; set; }
}
