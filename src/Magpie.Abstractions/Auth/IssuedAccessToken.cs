using System;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// An access token and its refresh-session identifier.
/// </summary>
/// <param name="Token">The access token</param>
/// <param name="ExpiresAt">The time when the access token expires</param>
/// <param name="Jti">The identifier to bind to the associated refresh token</param>
public sealed record IssuedAccessToken(string Token, DateTime ExpiresAt, string Jti);
