using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public record AuthProvider
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Provider { get; set; }
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ICollection<AuthProviderSetting> Settings { get; } = [];
    public ICollection<User> Users { get; } = [];
}
