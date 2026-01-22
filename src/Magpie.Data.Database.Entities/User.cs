using System;
using System.Collections.Generic;

using Magpie.Models.Users;

namespace Magpie.Data.Database.Entities;

public record User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public string? Email { get; set; }
    public Guid? AuthProviderId { get; set; }
    public string? ExternalId { get; set; }
    public bool Enabled { get; set; }
    public Role Role { get; set; }
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public AuthProvider? AuthProvider { get; set; }
    public ICollection<UserProviderData> ProviderData { get; } = [];
    public ICollection<Wishlist> Wishlists { get; } = [];
    public ICollection<ApiKey> ApiKeys { get; } = [];
}
