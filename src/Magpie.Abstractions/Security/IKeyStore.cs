using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

namespace Magpie.Abstractions.Security;

/// <summary>
/// Provides secure key storage. 
/// </summary>
public interface IKeyStore
{
    /// <summary>
    /// Checks if a key exists.
    /// </summary>
    Task<Result<bool>> KeyExistsAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new signing key for digital signatures.
    /// </summary>
    Task<Result<Unit>> CreateSigningKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new HMAC key.
    /// </summary>
    Task<Result<Unit>> CreateHmacKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new encryption key.
    /// </summary>
    Task<Result<Unit>> CreateEncryptionKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs data using a signing key.
    /// </summary>
    Task<Result<byte[]>> SignAsync(string keyName, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a signature.
    /// </summary>
    Task<Result<bool>> VerifySignatureAsync(
        string keyName,
        byte[] data,
        byte[] signature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes an HMAC.
    /// </summary>
    Task<Result<byte[]>> ComputeHmacAsync(string keyName, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an HMAC.
    /// </summary>
    Task<Result<bool>> VerifyHmacAsync(
        string keyName,
        byte[] data,
        byte[] hmac,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the public key material for external signature verification.
    /// </summary>
    Task<Result<byte[]>> GetPublicKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts data.
    /// </summary>
    Task<Result<byte[]>> EncryptAsync(string keyName, byte[] clearText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts data.
    /// </summary>
    Task<Result<byte[]>> DecryptAsync(string keyName, byte[] cipherText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a key.
    /// </summary>
    /// <returns><see langword="true"/> if the key was deleted, <see langword="false"/> if not found.</returns>
    Task<Result<bool>> DeleteKeyAsync(string keyName, CancellationToken cancellationToken = default);
}
