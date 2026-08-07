using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Security;

namespace Magpie.Abstractions.Security;

/// <summary>
/// The service contract for password hashes.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a password with a new salt.
    /// </summary>
    /// <param name="password">The plaintext password to hash</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The hash and the salt</returns>
    Task<HashedPassword> HashPasswordAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a password against a stored hash and salt.
    /// </summary>
    /// <param name="password">The plaintext password to validate</param>
    /// <param name="hash">The stored hash of the password</param>
    /// <param name="salt">The stored salt of the password</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the password matches the hash, otherwise <see langword="false"/></returns>
    Task<bool> VerifyPasswordAsync(
        string password,
        string hash,
        string salt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hashes a password without a salt.
    /// </summary>
    /// <param name="password">The plaintext password to hash</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The hash of the password</returns>
    Task<string> HashPasswordUnsaltedAsync(string password, CancellationToken cancellationToken = default);
}
