using System;

namespace Magpie.Data.Database.Entities;

public record UserProviderData
{
    public Guid UserId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public User? User { get; set; }
}
