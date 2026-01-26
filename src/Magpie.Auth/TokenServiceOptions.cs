using System;

namespace Magpie.Auth;

/// <summary>
/// Configuration options for the token service.
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
    /// The lifetime of access tokens.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The lifetime of refresh tokens.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);
}
