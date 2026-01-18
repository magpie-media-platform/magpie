using System;

namespace Magpie.Data.Database.Entities;

public class UserProviderData
{
    public Guid UserId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required User User { get; set; }
}
