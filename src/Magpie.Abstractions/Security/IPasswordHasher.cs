using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Security;

/// <summary>
/// Provides password hashing and verification functionality.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// A hashed password with a salt.
    /// </summary>
    /// <param name="Hash">The hashed password.</param>
    /// <param name="Salt">The salt used to hash the password.</param>
    public sealed record HashedPassword(string Hash, string Salt);

    /// <summary>
    /// Hash a password and generates a salt.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the hashed password and salt.</returns>
    Task<HashedPassword> HashPasswordAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a password against a stored hash and salt.
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="hash">The password hash.</param>
    /// <param name="salt">The password salt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the password matches the hash, otherwise <see langword="false"/>.</returns>
    Task<bool> VerifyPasswordAsync(
        string password,
        string hash,
        string salt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hash a password with no salt.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the hashed password, or a failure.</returns>
    Task<string> HashPasswordUnsaltedAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a password against a stored hash with no salt.
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="hash">The password hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the password matches the hash, otherwise <see langword="false"/>.</returns>
    Task<bool> VerifyPasswordUnsaltedAsync(
        string password,
        string hash,
        CancellationToken cancellationToken = default);
}
