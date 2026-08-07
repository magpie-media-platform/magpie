using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Security;

/// <summary>
/// A secure store of cryptographic keys.
/// </summary>
public interface IKeyStore
{
    /// <summary>
    /// Determines whether a key exists.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> KeyExistsAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a signing key.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task CreateSigningKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an HMAC key.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task CreateHmacKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs data with a signing key.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="data">The data to sign</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<byte[]> SignAsync(string keyName, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a signature against the data.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="data">The data that the signature covers</param>
    /// <param name="signature">The signature to verify</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> VerifySignatureAsync(
        string keyName,
        byte[] data,
        byte[] signature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes an HMAC over data.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="data">The data to authenticate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<byte[]> ComputeHmacAsync(string keyName, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an HMAC against the data.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="data">The data that the HMAC covers</param>
    /// <param name="hmac">The HMAC to verify</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> VerifyHmacAsync(
        string keyName,
        byte[] data,
        byte[] hmac,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the public key of a key pair.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<byte[]> GetPublicKeyAsync(string keyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a key.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteKeyAsync(string keyName, CancellationToken cancellationToken = default);
}
