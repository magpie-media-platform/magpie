using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Security;

using static Magpie.Abstractions.Security.IPasswordHasher;

namespace Magpie.Core.Security;

/// <summary>
/// Local password hasher.
/// </summary>
public sealed class LocalPasswordHasher(IKeyStore keyStore) : IPasswordHasher
{
    private const string ApiKeyHmacKeyName = "api-key-hmac";
    private const int SaltSize = 32;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    private readonly Lazy<Task> _ensureKeyTask = new(async () =>
    {
        var exists = await keyStore.KeyExistsAsync(ApiKeyHmacKeyName).ConfigureAwait(false);
        if (!exists)
        {
            await keyStore.CreateHmacKeyAsync(ApiKeyHmacKeyName).ConfigureAwait(false);
        }
    });

    /// <inheritdoc />
    public Task<HashedPassword> HashPasswordAsync(string password, CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return Task.FromResult(new HashedPassword(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt)));
    }

    /// <inheritdoc />
    public Task<bool> VerifyPasswordAsync(
        string password,
        string hash,
        string salt,
        CancellationToken cancellationToken)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var hashBytes = Convert.FromBase64String(hash);

        var computedHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return Task.FromResult(CryptographicOperations.FixedTimeEquals(computedHash, hashBytes));
    }

    /// <inheritdoc />
    public async Task<string> HashPasswordUnsaltedAsync(string password, CancellationToken cancellationToken)
    {
        await _ensureKeyTask.Value.ConfigureAwait(false);

        var hmac = await keyStore.ComputeHmacAsync(
            ApiKeyHmacKeyName,
            Encoding.UTF8.GetBytes(password),
            cancellationToken).ConfigureAwait(false);

        return Convert.ToBase64String(hmac);
    }

    /// <inheritdoc />
    public Task<bool> VerifyPasswordUnsaltedAsync(
        string passphrase,
        string hash,
        CancellationToken cancellationToken)
    {
        return keyStore.VerifyHmacAsync(
            ApiKeyHmacKeyName,
            Encoding.UTF8.GetBytes(passphrase),
            Convert.FromBase64String(hash),
            cancellationToken);
    }
}
