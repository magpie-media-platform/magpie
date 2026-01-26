using System;

namespace Magpie.Models.Users;

/// <summary>
/// Password credentials for a user.
/// </summary>
/// <param name="UserId">The user identifier.</param>
/// <param name="PasswordHash">The hashed password.</param>
/// <param name="PasswordSalt">The salt used for hashing.</param>
/// <param name="Enabled">Whether the user account is enabled.</param>
/// <param name="LockedUntil">When the user lockout expires, if locked.</param>
public sealed record UserCredentials(
    Guid UserId,
    string? PasswordHash,
    string? PasswordSalt,
    bool Enabled,
    DateTimeOffset? LockedUntil);
