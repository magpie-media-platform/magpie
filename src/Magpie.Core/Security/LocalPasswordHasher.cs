using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Security;
using Magpie.Models.Result;

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

    private readonly Lazy<Task<Result<Unit>>> _ensureKeyTask = new(() => keyStore.KeyExistsAsync(ApiKeyHmacKeyName)
        .BindAsync(exists => exists
            ? Result.SuccessAsync()
            : keyStore.CreateHmacKeyAsync(ApiKeyHmacKeyName)));

    /// <inheritdoc />
    public Task<Result<HashedPassword>> HashPasswordAsync(string password, CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return Result.SuccessAsync(new HashedPassword(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt)));
    }

    /// <inheritdoc />
    public Task<Result<bool>> VerifyPasswordAsync(
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

        return Result.SuccessAsync(CryptographicOperations.FixedTimeEquals(computedHash, hashBytes));
    }

    /// <inheritdoc />
    public Task<Result<string>> HashPasswordUnsaltedAsync(string password, CancellationToken cancellationToken)
    {
        return _ensureKeyTask.Value
            .BindAsync(_ => keyStore.ComputeHmacAsync(
                ApiKeyHmacKeyName,
                Encoding.UTF8.GetBytes(password),
                cancellationToken))
            .MapAsync(Convert.ToBase64String);

    }

    /// <inheritdoc />
    public Task<Result<bool>> VerifyPasswordUnsaltedAsync(
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
