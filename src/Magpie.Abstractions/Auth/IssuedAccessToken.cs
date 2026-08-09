using System;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// An access token and its refresh-session identifier.
/// </summary>
/// <param name="Token">Token value</param>
/// <param name="ExpiresAt">Expiration timestamp</param>
/// <param name="Jti">Identifier to bind to the associated refresh token</param>
public sealed record IssuedAccessToken(string Token, DateTime ExpiresAt, string Jti);
