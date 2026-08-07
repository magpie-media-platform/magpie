using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of a Magpie user account.
/// </summary>
public sealed record UserResponse
{
    /// <summary>
    /// The id of the user.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The username of the account.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The email address of the user.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// The id of the extension that provides the authentication provider.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The provider type that authenticates the user.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The id of the user in the authentication provider.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// The provider-specific settings for the user.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// Whether the user is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The role of the user.
    /// </summary>
    public Role Role { get; init; }

    /// <summary>
    /// The number of consecutive failed login attempts.
    /// </summary>
    public int FailedLoginAttempts { get; init; }

    /// <summary>
    /// The time when the lockout of the account ends.
    /// </summary>
    public DateTime? LockedUntil { get; init; }

    /// <summary>
    /// The time of the last login of the user.
    /// </summary>
    public DateTime? LastLoginAt { get; init; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// The UTC time when the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; init; }
}
