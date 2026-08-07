using System;

using Microsoft.IdentityModel.Tokens;

namespace Magpie.Auth;

internal sealed class KeyStoreHmacSignatureProvider : SignatureProvider
{
    private readonly KeyStoreHmacSecurityKey _key;
    private bool _disposed;

    public KeyStoreHmacSignatureProvider(
        KeyStoreHmacSecurityKey key,
        string algorithm,
        bool willCreateSignatures) : base(key, algorithm)
    {
        _key = key;
        WillCreateSignatures = willCreateSignatures;
    }

    public override byte[] Sign(byte[] input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!WillCreateSignatures)
        {
            throw new InvalidOperationException("The signature provider cannot create signatures");
        }

        return _key.KeyStore.ComputeHmacAsync(_key.KeyName, input)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    public override bool Verify(byte[] input, byte[] signature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _key.KeyStore.VerifyHmacAsync(_key.KeyName, input, signature)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
    }
}
