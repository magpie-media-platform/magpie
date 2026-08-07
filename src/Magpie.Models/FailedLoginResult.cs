using System;

namespace Magpie.Models.Repositories;

/// <summary>
/// The result of an account update after a failed login.
/// </summary>
/// <param name="FailedAttempts">The new count of consecutive failed logins</param>
/// <param name="LockedUntil">The time when the lock ends, or <see langword="null"/> when not locked</param>
public sealed record FailedLoginResult(int FailedAttempts, DateTime? LockedUntil);
