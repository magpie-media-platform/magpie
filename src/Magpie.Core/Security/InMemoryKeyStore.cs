using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Security;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Security;

/// <summary>
/// In-memory key store implementation.
/// </summary>
public sealed class InMemoryKeyStore(ILogger<InMemoryKeyStore> logger) : IKeyStore
{
    private enum KeyType { Signing, Hmac, Encryption }

    private readonly ConcurrentDictionary<string, KeyEntry> _keys = new();

    private sealed record KeyEntry(byte[] KeyMaterial, KeyType Type);

    /// <inheritdoc />
    public Task<bool> KeyExistsAsync(string keyName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_keys.ContainsKey(keyName));
    }

    /// <inheritdoc />
    public Task<bool> DeleteKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        var removed = _keys.TryRemove(keyName, out _);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task CreateSigningKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        CreateKey(keyName, KeyType.Signing, 32);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> SignAsync(string keyName, byte[] data, CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyType.Signing);
        using var hmac = new HMACSHA256(entry.KeyMaterial);
        var signature = hmac.ComputeHash(data);
        return Task.FromResult(signature);
    }

    /// <inheritdoc />
    public Task<bool> VerifySignatureAsync(
        string keyName,
        byte[] data,
        byte[] signature,
        CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyType.Signing);
        using var hmac = new HMACSHA256(entry.KeyMaterial);
        var computed = hmac.ComputeHash(data);
        var valid = CryptographicOperations.FixedTimeEquals(computed, signature);
        return Task.FromResult(valid);
    }

    /// <inheritdoc />
    public Task<byte[]> GetPublicKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyType.Signing);
        return Task.FromResult(entry.KeyMaterial);
    }

    /// <inheritdoc />
    public Task CreateHmacKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        CreateKey(keyName, KeyType.Hmac, 32);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> ComputeHmacAsync(
        string keyName,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyType.Hmac);
        using var hmac = new HMACSHA256(entry.KeyMaterial);
        var hash = hmac.ComputeHash(data);
        return Task.FromResult(hash);
    }

    /// <inheritdoc />
    public Task<bool> VerifyHmacAsync(
        string keyName,
        byte[] data,
        byte[] hmac,
        CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyType.Hmac);
        using var hmacAlg = new HMACSHA256(entry.KeyMaterial);
        var computed = hmacAlg.ComputeHash(data);
        var valid = CryptographicOperations.FixedTimeEquals(computed, hmac);
        return Task.FromResult(valid);
    }

    /// <inheritdoc />
    public Task CreateEncryptionKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        CreateKey(keyName, KeyType.Encryption, 32);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> EncryptAsync(
        string keyName,
        byte[] plaintext,
        CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyType.Encryption);
        using var aes = Aes.Create();
        aes.Key = entry.KeyMaterial;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        var result = new byte[aes.IV.Length + encrypted.Length];

        aes.IV.CopyTo(result, 0);
        encrypted.CopyTo(result, aes.IV.Length);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<byte[]> DecryptAsync(
        string keyName,
        byte[] ciphertext,
        CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyType.Encryption);

        try
        {
            using var aes = Aes.Create();
            aes.Key = entry.KeyMaterial;

            var iv = new byte[aes.BlockSize / 8];
            Array.Copy(ciphertext, 0, iv, 0, iv.Length);
            aes.IV = iv;

            var encrypted = new byte[ciphertext.Length - iv.Length];
            Array.Copy(ciphertext, iv.Length, encrypted, 0, encrypted.Length);

            using var decryptor = aes.CreateDecryptor();
            var plaintext = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

            return Task.FromResult(plaintext);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Decryption failed for key '{KeyName}'", keyName);
            throw new InvalidOperationException($"Decryption failed for key '{keyName}'", ex);
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

    private void CreateKey(string keyName, KeyType type, int sizeBytes)
    {
        var keyBytes = new byte[sizeBytes];
        RandomNumberGenerator.Fill(keyBytes);

        if (!_keys.TryAdd(keyName, new KeyEntry(keyBytes, type)))
        {
            throw new InvalidOperationException($"Key '{keyName}' already exists");
        }
    }

    private KeyEntry GetKeyOrThrow(string keyName, KeyType expectedType)
    {
        if (!_keys.TryGetValue(keyName, out var entry))
        {
            throw new InvalidOperationException($"Key '{keyName}' not found");
        }

        if (entry.Type != expectedType)
        {
            throw new InvalidOperationException($"Key '{keyName}' type mismatch");
        }

        return entry;
    }
}
