using System;

namespace Magpie.Auth;

/// <summary>
/// The options for the Magpie token service.
/// </summary>
public sealed class TokenServiceOptions
{
    /// <summary>
    /// The issuer claim for JWT tokens.
    /// </summary>
    public string Issuer { get; set; } = "magpie";

    /// <summary>
    /// The audience claim for JWT tokens.
    /// </summary>
    public string Audience { get; set; } = "magpie";

    /// <summary>
    /// The access token lifetime.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The refresh token lifetime.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// The lockout options for user accounts.
    /// </summary>
    public AccountLockoutOptions LockoutOptions { get; set; } = new AccountLockoutOptions();
}
