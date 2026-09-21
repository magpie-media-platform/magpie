using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Security;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Security;

internal sealed class AtomicFileKeyStore(
    MagpieHostEnvironment hostEnvironment,
    ILogger<AtomicFileKeyStore> logger) : IKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private const string FileExtension = ".key";

    private readonly ConcurrentDictionary<string, KeyStoreEntry> _cache = new();

    internal enum KeyStoreKeyType
    {
        Signing,
        Hmac,
    }

    public string KeysDirectory { get; } = Path.Combine(hostEnvironment.DataRoot, "keys");

    public Task<bool> KeyExistsAsync(string keyName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TryLoad(keyName, out _));
    }

    public Task<bool> DeleteKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        var path = PathFor(keyName);
        var existed = File.Exists(path);
        if (existed)
        {
            File.Delete(path);
        }

        _cache.TryRemove(keyName, out _);
        return Task.FromResult(existed);
    }

    public Task CreateSigningKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        CreateKey(keyName, KeyStoreKeyType.Signing, 32);
        return Task.CompletedTask;
    }

    public Task CreateHmacKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        CreateKey(keyName, KeyStoreKeyType.Hmac, 32);
        return Task.CompletedTask;
    }

    public Task<byte[]> SignAsync(string keyName, byte[] data, CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyStoreKeyType.Signing);
        using var hmac = new HMACSHA256(entry.KeyMaterial);
        return Task.FromResult(hmac.ComputeHash(data));
    }

    public Task<bool> VerifySignatureAsync(
        string keyName,
        byte[] data,
        byte[] signature,
        CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyStoreKeyType.Signing);
        using var hmac = new HMACSHA256(entry.KeyMaterial);
        var computed = hmac.ComputeHash(data);
        return Task.FromResult(CryptographicOperations.FixedTimeEquals(computed, signature));
    }

    public Task<byte[]> GetPublicKeyAsync(string keyName, CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyStoreKeyType.Signing);
        return Task.FromResult(entry.KeyMaterial);
    }

    public Task<byte[]> ComputeHmacAsync(string keyName, byte[] data, CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyStoreKeyType.Hmac);
        using var hmac = new HMACSHA256(entry.KeyMaterial);
        return Task.FromResult(hmac.ComputeHash(data));
    }

    public Task<bool> VerifyHmacAsync(
        string keyName,
        byte[] data,
        byte[] hmac,
        CancellationToken cancellationToken = default)
    {
        var entry = GetKeyOrThrow(keyName, KeyStoreKeyType.Hmac);
        using var hmacAlgorithm = new HMACSHA256(entry.KeyMaterial);
        var computed = hmacAlgorithm.ComputeHash(data);
        return Task.FromResult(CryptographicOperations.FixedTimeEquals(computed, hmac));
    }

    private static void EnsureKeyDirectory(string path, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        TryRestrictDirectory(path, logger);
    }

    private static FileStream CreateRestrictedKeyFile(string path)
    {
        var options = new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.CreateNew,
            Options = FileOptions.WriteThrough,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    [UnsupportedOSPlatform("windows")]
    private static void TryRestrictDirectory(string path, ILogger logger)
    {
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "The key store failed to restrict directory {DirectoryPath}", path);
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void TryRestrictFile(string path, ILogger logger)
    {
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "The key store failed to restrict file {FilePath}", path);
        }
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The key store failed to delete temporary file {FilePath}", path);
        }
    }

    private static void WriteKeyFileAtomically(string path, string contents, ILogger logger)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Key path must include a directory.", nameof(path));

        EnsureKeyDirectory(directory, logger);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Utf8NoBom.GetBytes(contents);
            using (var stream = CreateRestrictedKeyFile(temporaryPath))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            RestrictExistingKeyFile(temporaryPath, logger);
            File.Move(temporaryPath, path, overwrite: false);
            RestrictExistingKeyFile(path, logger);
        }
        catch
        {
            TryDelete(temporaryPath, logger);
            throw;
        }
    }

    private static void RestrictExistingKeyFile(string path, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        TryRestrictFile(path, logger);
    }

    private void CreateKey(string keyName, KeyStoreKeyType type, int sizeBytes)
    {
        var path = PathFor(keyName);
        if (File.Exists(path) || _cache.ContainsKey(keyName))
        {
            throw new InvalidOperationException($"Key {keyName} already exists");
        }

        var keyBytes = RandomNumberGenerator.GetBytes(sizeBytes);
        var envelope = new KeyEnvelope(type, Convert.ToBase64String(keyBytes));
        WriteKeyFileAtomically(path, JsonSerializer.Serialize(envelope, JsonOptions), logger);
        _cache[keyName] = new KeyStoreEntry(keyBytes, type);
    }

    private KeyStoreEntry GetKeyOrThrow(string keyName, KeyStoreKeyType expectedType)
    {
        if (!TryLoad(keyName, out var entry))
        {
            throw new InvalidOperationException($"Key {keyName} not found");
        }

        if (entry.Type != expectedType)
        {
            throw new InvalidOperationException($"Key {keyName} type mismatch");
        }

        return entry;
    }

    private bool TryLoad(string keyName, out KeyStoreEntry entry)
    {
        if (_cache.TryGetValue(keyName, out var cached))
        {
            entry = cached;
            return true;
        }

        var path = PathFor(keyName);
        if (!File.Exists(path))
        {
            entry = null!;
            return false;
        }

        try
        {
            RestrictExistingKeyFile(path, logger);
            var envelope = JsonSerializer.Deserialize<KeyEnvelope>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"Key {keyName} file is empty");
            var loaded = new KeyStoreEntry(Convert.FromBase64String(envelope.Material), envelope.Type);
            entry = _cache.GetOrAdd(keyName, loaded);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new InvalidOperationException($"Key {keyName} file is corrupt", ex);
        }
    }

    private string PathFor(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            throw new ArgumentException("Key name cannot be empty", nameof(keyName));
        }

        var containsReservedPathCharacters =
            keyName.Contains('/', StringComparison.Ordinal) || keyName.Contains('\\', StringComparison.Ordinal);
        if (containsReservedPathCharacters || keyName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Key name {keyName} has an invalid format.", nameof(keyName));
        }

        return Path.Combine(KeysDirectory, keyName + FileExtension);
    }

    private sealed record KeyEnvelope(KeyStoreKeyType Type, string Material);

    private sealed record KeyStoreEntry(byte[] KeyMaterial, KeyStoreKeyType Type);
}
