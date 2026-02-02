using System.Threading;
using System.Threading.Tasks;


namespace Magpie.Abstractions.Security;

/// <summary>
/// Provides secure key storage. 
/// </summary>
public interface IKeyStore
{
    /// <summary>
    /// Checks if a key exists.
    /// </summary>
    Task<bool> KeyExistsAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new signing key for digital signatures.
    /// </summary>
    Task CreateSigningKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new HMAC key.
    /// </summary>
    Task CreateHmacKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new encryption key.
    /// </summary>
    Task CreateEncryptionKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs data using a signing key.
    /// </summary>
    Task<byte[]> SignAsync(string keyName, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a signature.
    /// </summary>
    Task<bool> VerifySignatureAsync(
        string keyName,
        byte[] data,
        byte[] signature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute an HMAC.
    /// </summary>
    Task<byte[]> ComputeHmacAsync(string keyName, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify an HMAC.
    /// </summary>
    Task<bool> VerifyHmacAsync(
        string keyName,
        byte[] data,
        byte[] hmac,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the public key material for external signature verification.
    /// </summary>
    Task<byte[]> GetPublicKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts data.
    /// </summary>
    Task<byte[]> EncryptAsync(string keyName, byte[] clearText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts data.
    /// </summary>
    Task<byte[]> DecryptAsync(string keyName, byte[] cipherText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a key.
    /// </summary>
    /// <returns><see langword="true"/> if the key was deleted, <see langword="false"/> if not found.</returns>
    Task<bool> DeleteKeyAsync(string keyName, CancellationToken cancellationToken = default);
}
