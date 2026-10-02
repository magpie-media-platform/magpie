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
    /// Creates an HMAC key.
    /// </summary>
    /// <param name="keyName">The name of the stored key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task CreateHmacKeyAsync(string keyName, CancellationToken cancellationToken = default);

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
}
