using System;

namespace Magpie.Data.Database.Entities;

public class AuthProviderSetting
{
    public Guid Id { get; set; }
    public Guid AuthProviderId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required AuthProvider AuthProvider { get; set; }
}
