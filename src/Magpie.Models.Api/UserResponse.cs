using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// API response for a user (excludes credential fields).
/// </summary>
public sealed record UserResponse
{
    /// <summary>
    /// The user id.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The username.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The email address.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// The extension that defines the authentication provider.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The type of authentication provider used to authenticate this user.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The external identifier from the authentication provider.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// Provider-specific settings for this user.
    /// </summary>
    public Dictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// Whether the user account is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The user's role.
    /// </summary>
    public Role Role { get; init; }

    /// <summary>
    /// The number of consecutive failed login attempts.
    /// </summary>
    public int FailedLoginAttempts { get; init; }

    /// <summary>
    /// When the account lockout expires.
    /// </summary>
    public DateTime? LockedUntil { get; init; }

    /// <summary>
    /// When the user last logged in.
    /// </summary>
    public DateTime? LastLoginAt { get; init; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; init; }
}
