using System;

namespace Magpie.Models.Security;

/// <summary>
/// A token and the time when it expires.
/// </summary>
/// <param name="Token">The token</param>
/// <param name="ExpiresAt">The time when the token expires</param>
public sealed record IssuedToken(string Token, DateTime ExpiresAt);
