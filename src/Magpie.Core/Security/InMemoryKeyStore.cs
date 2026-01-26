using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Security;
using Magpie.Models.Result;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Security;

/// <summary>
/// In-memory key store implementation.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="InMemoryKeyStore"/> class.
/// </remarks>
public sealed class InMemoryKeyStore(ILogger<InMemoryKeyStore> logger) : IKeyStore
{
    private enum KeyType { Signing, Hmac, Encryption }

    private readonly ConcurrentDictionary<string, KeyEntry> _keys = new();

    private sealed record KeyEntry(byte[] KeyMaterial, KeyType Type);

    /// <inheritdoc />
    public Task<Result<bool>> KeyExistsAsync(string keyName, CancellationToken cancellationToken = default)
    {
        return Result.SuccessAsync(_keys.ContainsKey(keyName));
    }

    /// <inheritdoc />
    public Task<Result<bool>> DeleteKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        var removed = _keys.TryRemove(keyName, out _);
        return Result.SuccessAsync(removed);
    }

    /// <inheritdoc />
    public Task<Result<Unit>> CreateSigningKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        return CreateKey(keyName, KeyType.Signing, 32);
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> SignAsync(string keyName, byte[] data, CancellationToken cancellationToken = default)
    {
        if (!TryGetKey<byte[]>(keyName, KeyType.Signing, out var entry, out var error))
        {
            return Task.FromResult(error!);
        }
        using var hmac = new HMACSHA256(entry!.KeyMaterial);

        var signature = hmac.ComputeHash(data);

        return Result.SuccessAsync(signature);
    }

    /// <inheritdoc />
    public Task<Result<bool>> VerifySignatureAsync(
        string keyName,
        byte[] data,
        byte[] signature,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetKey<bool>(keyName, KeyType.Signing, out var entry, out var error))
        {
            return Task.FromResult(error!);
        }
        using var hmac = new HMACSHA256(entry!.KeyMaterial);

        var computed = hmac.ComputeHash(data);
        var valid = CryptographicOperations.FixedTimeEquals(computed, signature);

        return Result.SuccessAsync(valid);
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> GetPublicKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        if (!TryGetKey<byte[]>(keyName, KeyType.Signing, out var entry, out var error))
        {
            return Task.FromResult(error!);
        }
        return Result.SuccessAsync(entry!.KeyMaterial);
    }

    /// <inheritdoc />
    public Task<Result<Unit>> CreateHmacKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        return CreateKey(keyName, KeyType.Hmac, 32);
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> ComputeHmacAsync(
        string keyName,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetKey<byte[]>(keyName, KeyType.Hmac, out var entry, out var error))
        {
            return Task.FromResult(error!);
        }
        using var hmac = new HMACSHA256(entry!.KeyMaterial);

        var hash = hmac.ComputeHash(data);

        return Result.SuccessAsync(hash);
    }

    /// <inheritdoc />
    public Task<Result<bool>> VerifyHmacAsync(
        string keyName,
        byte[] data,
        byte[] hmac,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetKey<bool>(keyName, KeyType.Hmac, out var entry, out var error))
        {
            return Task.FromResult(error!);
        }
        using var hmacAlg = new HMACSHA256(entry!.KeyMaterial);

        var computed = hmacAlg.ComputeHash(data);
        var valid = CryptographicOperations.FixedTimeEquals(computed, hmac);

        return Result.SuccessAsync(valid);
    }

    /// <inheritdoc />
    public Task<Result<Unit>> CreateEncryptionKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        return CreateKey(keyName, KeyType.Encryption, 32);
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> EncryptAsync(
        string keyName,
        byte[] plaintext,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetKey<byte[]>(keyName, KeyType.Encryption, out var entry, out var error))
        {
            return Task.FromResult(error!);
        }
        using var aes = Aes.Create();
        using var encryptor = aes.CreateEncryptor();

        aes.Key = entry!.KeyMaterial;
        aes.GenerateIV();

        var encrypted = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        var result = new byte[aes.IV.Length + encrypted.Length];

        aes.IV.CopyTo(result, 0);
        encrypted.CopyTo(result, aes.IV.Length);

        return Result.SuccessAsync(result);
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> DecryptAsync(
        string keyName,
        byte[] ciphertext,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetKey<byte[]>(keyName, KeyType.Encryption, out var entry, out var error))
        {
            return Task.FromResult(error!);
        }

        try
        {
            using var aes = Aes.Create();
            using var decryptor = aes.CreateDecryptor();

            aes.Key = entry!.KeyMaterial;

            var iv = new byte[aes.BlockSize / 8];
            Array.Copy(ciphertext, 0, iv, 0, iv.Length);
            aes.IV = iv;

            var encrypted = new byte[ciphertext.Length - iv.Length];
            Array.Copy(ciphertext, iv.Length, encrypted, 0, encrypted.Length);

            var plaintext = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

            return Result.SuccessAsync(plaintext);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Decryption failed for key '{KeyName}'", keyName);
            return Result.FailAsync<byte[]>("Decryption failed", ErrorCodes.DecryptionFailed);
        }
    }

    /// <summary>
    /// Preloads a signing key from configuration.
    /// </summary>
    public void LoadSigningKey(string keyName, byte[] keyMaterial)
    {
        _keys[keyName] = new KeyEntry(keyMaterial, KeyType.Signing);
    }

    /// <summary>
    /// Preloads an HMAC key from configuration.
    /// </summary>
    public void LoadHmacKey(string keyName, byte[] keyMaterial)
    {
        _keys[keyName] = new KeyEntry(keyMaterial, KeyType.Hmac);
    }

    /// <summary>
    /// Preloads an encryption key from configuration.
    /// </summary>
    public void LoadEncryptionKey(string keyName, byte[] keyMaterial)
    {
        _keys[keyName] = new KeyEntry(keyMaterial, KeyType.Encryption);
    }

    private Task<Result<Unit>> CreateKey(string keyName, KeyType type, int sizeBytes)
    {
        var keyBytes = new byte[sizeBytes];
        RandomNumberGenerator.Fill(keyBytes);

        if (!_keys.TryAdd(keyName, new KeyEntry(keyBytes, type)))
        {
            return Result.FailAsync<Unit>($"Key '{keyName}' already exists", ErrorCodes.KeyAlreadyExists);
        }

        return Result.SuccessAsync();
    }

    private bool TryGetKey<T>(string keyName, KeyType expectedType, out KeyEntry? entry, out Result<T>? error)
    {
        if (!_keys.TryGetValue(keyName, out entry))
        {
            error = Result.Fail<T>($"Key '{keyName}' not found", ErrorCodes.KeyNotFound);
            return false;
        }
        if (entry.Type != expectedType)
        {
            error = Result.Fail<T>($"Key '{keyName}' type mismatch", ErrorCodes.KeyTypeMismatch);
            entry = null;
            return false;
        }
        error = null;
        return true;
    }
}
