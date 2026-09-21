using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Security;
using Magpie.Models.Security;
using Magpie.Sdk.Security;

namespace Magpie.Core.Security;

internal sealed class LocalPasswordHasher(IKeyStore keyStore) : IPasswordHasher
{
    private const string ApiKeyHmacKeyName = "api-key-hmac";
    private const int SaltSize = 32;
    private const int HashSize = 32;

    private const int Iterations = 600_000;

    private const string IterationPrefix = "i=";
    private const char IterationSeparator = '$';

    private readonly Lazy<Task> _ensureKeyTask = new(async () =>
    {
        var exists = await keyStore.KeyExistsAsync(ApiKeyHmacKeyName).ConfigureAwait(false);
        if (!exists)
        {
            try
            {
                await keyStore.CreateHmacKeyAsync(ApiKeyHmacKeyName).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                if (!await keyStore.KeyExistsAsync(ApiKeyHmacKeyName).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }
    });

    private static string EncodeHash(int iterations, byte[] hash)
    {
        return $"{IterationPrefix}{iterations}{IterationSeparator}{Convert.ToBase64String(hash)}";
    }

    private static (int Iterations, byte[] Hash) DecodeHash(string encoded)
    {
        if (encoded.StartsWith(IterationPrefix, StringComparison.Ordinal))
        {
            var separatorIndex = encoded.IndexOf(IterationSeparator, IterationPrefix.Length);
            if (separatorIndex > IterationPrefix.Length &&
                int.TryParse(
                    encoded.AsSpan(IterationPrefix.Length, separatorIndex - IterationPrefix.Length),
                    out var iterations) && iterations > 0)
            {
                return (iterations, Convert.FromBase64String(encoded[(separatorIndex + 1)..]));
            }
        }

        throw new FormatException(
            $"Stored password hash is not in the expected '{IterationPrefix}<iterations>{IterationSeparator}<hash>' " +
            "format.");
    }

    public Task<HashedPassword> HashPasswordAsync(string password, CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return Task.FromResult(new HashedPassword(EncodeHash(Iterations, hash), Convert.ToBase64String(salt)));
    }

    public Task<bool> VerifyPasswordAsync(
        string password,
        string hash,
        string salt,
        CancellationToken cancellationToken)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var (iterations, hashBytes) = DecodeHash(hash);

        var computedHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return Task.FromResult(CryptographicOperations.FixedTimeEquals(computedHash, hashBytes));
    }

    public async Task<string> HashPasswordUnsaltedAsync(string password, CancellationToken cancellationToken)
    {
        await _ensureKeyTask.Value.ConfigureAwait(false);

        var hmac = await keyStore.ComputeHmacAsync(
            ApiKeyHmacKeyName,
            Encoding.UTF8.GetBytes(password),
            cancellationToken).ConfigureAwait(false);

        return Convert.ToBase64String(hmac);
    }
}
