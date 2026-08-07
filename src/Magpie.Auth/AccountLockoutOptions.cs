using System;

namespace Magpie.Auth;

/// <summary>
/// The account lockout options for local credential authentication.
/// </summary>
public sealed class AccountLockoutOptions
{
    /// <summary>
    /// Whether the lockout policy is enforced.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The number of consecutive failed attempts that causes a lockout.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// The time that the account stays locked after a lockout.
    /// </summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}
