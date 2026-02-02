using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A user account.
/// </summary>
public record User
{
    /// <summary>
    /// The user id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The username.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// The email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// The extension that defines the authentication provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The type of authentication provider used to authenticate this user.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The external identifier from the authentication provider.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Provider-specific settings for this user.
    /// </summary>
    /// <remarks>
    /// The data is stored as serialized JSON.
    /// </remarks>
    public Dictionary<string, string>? Settings { get; set; }

    /// <summary>
    /// Whether the user account is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The user's role.
    /// </summary>
    public Role Role { get; set; }

    /// <summary>
    /// The hashed password for local authentication.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// The password salt for local authentication.
    /// </summary>
    public string? PasswordSalt { get; set; }

    /// <summary>
    /// The number of consecutive failed login attempts.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// When the account lockout expires.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// When the user last logged in.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// API keys for this user.
    /// </summary>
    public ICollection<ApiKey> ApiKeys { get; } = [];

    /// <summary>
    /// Refresh tokens for this user.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; } = [];
}
