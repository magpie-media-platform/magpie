using System;

namespace Magpie.Models.Security;

/// <summary>
/// A token value and expiration.
/// </summary>
/// <param name="Token">Token value</param>
/// <param name="ExpiresAt">Expiration timestamp</param>
public sealed record IssuedToken(string Token, DateTime ExpiresAt);
