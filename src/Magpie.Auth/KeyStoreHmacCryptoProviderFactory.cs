using System;

using Microsoft.IdentityModel.Tokens;

namespace Magpie.Auth;

internal sealed class KeyStoreHmacCryptoProviderFactory : CryptoProviderFactory
{
    private static KeyStoreHmacSignatureProvider CreateProvider(
        SecurityKey key,
        string algorithm,
        bool willCreateSignatures)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key is not KeyStoreHmacSecurityKey hmacKey || !IsSupportedHmacAlgorithm(algorithm))
        {
            throw new NotSupportedException("The key and algorithm combination is not supported");
        }

        return new KeyStoreHmacSignatureProvider(hmacKey, algorithm, willCreateSignatures);
    }

    private static bool IsSupportedHmacAlgorithm(string algorithm)
    {
        return string.Equals(algorithm, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal) ||
            string.Equals(algorithm, SecurityAlgorithms.HmacSha256Signature, StringComparison.Ordinal);
    }

    public override SignatureProvider CreateForSigning(SecurityKey key, string algorithm, bool cacheProvider)
    {
        return CreateProvider(key, algorithm, willCreateSignatures: true);
    }

    public override SignatureProvider CreateForVerifying(SecurityKey key, string algorithm, bool cacheProvider)
    {
        return CreateProvider(key, algorithm, willCreateSignatures: false);
    }

    public override bool IsSupportedAlgorithm(string algorithm, SecurityKey key)
    {
        return key is KeyStoreHmacSecurityKey && IsSupportedHmacAlgorithm(algorithm);
    }
}
