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
    /// The unique id of the user.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name that the user signs in with.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// The email address of the user.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// The id of the extension that supplies the provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The name of the provider type that authenticates the user.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The id of the user at the authentication provider.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The provider settings for the user, keyed by name.
    /// </summary>
    public Dictionary<string, string>? Settings { get; set; }

    /// <summary>
    /// Whether the user can sign in.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The access level of the user, from <see cref="Enums.Role"/>.
    /// </summary>
    public Role Role { get; set; }

    /// <summary>
    /// The hash of the password for local authentication.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// The salt of the password for local authentication.
    /// </summary>
    public string? PasswordSalt { get; set; }

    /// <summary>
    /// The number of failed sign-in attempts since the last success.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// The UTC time until which the account stays locked.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// The UTC time of the last successful sign-in.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The API keys of the user.
    /// </summary>
    public ICollection<ApiKey> ApiKeys { get; } = [];

    /// <summary>
    /// The refresh tokens of the user.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; } = [];
}
