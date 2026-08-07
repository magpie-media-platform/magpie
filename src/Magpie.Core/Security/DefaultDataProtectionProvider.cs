using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Magpie.Sdk.Security;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Security;

internal sealed class DefaultDataProtectionProvider : IDataProtectionProvider
{
    private const string EnvironmentVariableName = "MAGPIE_SECRET_KEY";

    private const int KeySizeBytes = 32;
    private const string KeyFileName = "integration-secret.key";
    private const string FormatPrefix = "v1";

    private readonly Dictionary<string, ProtectionKey> _keysById = [];
    private readonly ILogger<DefaultDataProtectionProvider> _logger;

    public DefaultDataProtectionProvider(
        IKeyStore keyStore,
        ILogger<DefaultDataProtectionProvider> logger)
    {
        _logger = logger;
        var atomicKeyStore = keyStore as IAtomicKeyStore
            ?? throw new InvalidOperationException("The default data protection provider requires atomic key storage.");
        var keyFilePath = Path.Combine(atomicKeyStore.KeysDirectory, KeyFileName);

        var environmentValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        ProtectionKey? environmentKey = null;
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            environmentKey = CreateKey(ParseEnvironmentKey(environmentValue));
            Register(environmentKey);
        }

        if (environmentKey is null)
        {
            var fileKey = CreateKey(LoadOrCreateFileMaterial(atomicKeyStore, keyFilePath));
            Register(fileKey);
            ActiveKeyId = fileKey.Id;
            _logger.LogInformation(
                "The data protection provider selected the file-backed key {KeyId} to protect sensitive data",
                fileKey.Id);
        }
        else
        {
            ActiveKeyId = environmentKey.Id;
            if (File.Exists(keyFilePath))
            {
                Register(CreateKey(ReadFileMaterial(atomicKeyStore, keyFilePath)));
            }

            _logger.LogInformation(
                "The data protection provider selected the {EnvironmentVariableName} key {KeyId} " +
                "to protect sensitive data",
                EnvironmentVariableName,
                environmentKey.Id);
        }
    }

    public string ActiveKeyId { get; }

    private static byte[] ParseEnvironmentKey(string value)
    {
        byte[] material;
        try
        {
            material = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Environment variable {EnvironmentVariableName} must be a base64-encoded {KeySizeBytes}-byte key.",
                ex);
        }

        if (material.Length != KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"Environment variable {EnvironmentVariableName} must decode to {KeySizeBytes} bytes " +
                $"but decoded to {material.Length}.");
        }

        return material;
    }

    private static byte[] LoadOrCreateFileMaterial(IAtomicKeyStore keyStore, string keyFilePath)
    {
        if (File.Exists(keyFilePath))
        {
            return ReadFileMaterial(keyStore, keyFilePath);
        }

        var material = RandomNumberGenerator.GetBytes(KeySizeBytes);
        keyStore.WriteKeyFileAtomically(keyFilePath, Convert.ToBase64String(material));
        return material;
    }

    private static byte[] ReadFileMaterial(IAtomicKeyStore keyStore, string keyFilePath)
    {
        byte[] material;
        try
        {
            keyStore.RestrictExistingKeyFile(keyFilePath);
            material = Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Data protection key file {keyFilePath} is corrupt.", ex);
        }

        if (material.Length != KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"Data protection key file {keyFilePath} must contain a {KeySizeBytes}-byte key.");
        }

        return material;
    }

    private static ProtectionKey CreateKey(byte[] material)
    {
        var hash = SHA256.HashData(material);
        var keyId = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return new ProtectionKey(keyId, material);
    }

    private static bool TryReadEnvelope(string protectedValue, out string keyId, out byte[] payload)
    {
        keyId = string.Empty;
        payload = [];

        var parts = protectedValue.Split(':', 3);
        if (parts.Length != 3 || !string.Equals(parts[0], FormatPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            payload = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        keyId = parts[1];
        return true;
    }

    public string Protect(string value)
    {
        var key = _keysById[ActiveKeyId];
        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(key.Material, tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return $"{FormatPrefix}:{key.Id}:{Convert.ToBase64String(payload)}";
    }

    public string Unprotect(string protectedValue)
    {
        if (!TryReadEnvelope(protectedValue, out var keyId, out var payload))
        {
            throw new InvalidOperationException("The protected value has an invalid format.");
        }

        if (!_keysById.TryGetValue(keyId, out var key))
        {
            throw new InvalidOperationException($"No data protection key is available for key id {keyId}.");
        }

        try
        {
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            if (payload.Length < nonceSize + tagSize)
            {
                throw new InvalidOperationException("The protected value is too short to be valid.");
            }

            var nonce = payload[..nonceSize];
            var tag = payload[nonceSize..(nonceSize + tagSize)];
            var ciphertext = payload[(nonceSize + tagSize)..];
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key.Material, tag.Length))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "The data protection provider failed to unprotect data for key id {KeyId}", keyId);
            throw new InvalidOperationException($"Failed to unprotect data for key id {keyId}.", ex);
        }
    }

    public bool TryGetKeyId(string protectedValue, out string keyId)
    {
        return TryReadEnvelope(protectedValue, out keyId, out _);
    }

    private void Register(ProtectionKey key)
    {
        _keysById[key.Id] = key;
    }

    private sealed record ProtectionKey(string Id, byte[] Material);
}
