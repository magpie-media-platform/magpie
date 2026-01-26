using System;
using System.Security.Cryptography;

using Magpie.Abstractions.Security;

namespace Magpie.Core.Security;

/// <inheritdoc />
public sealed class KeyGenerator : IKeyGenerator
{
    /// <inheritdoc />
    public string GenerateKey(string prefix, int length)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(length);
        var encoded = Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        return prefix + encoded;
    }
}
