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
    /// Hashes a password and generates a salt.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    HashedPassword HashPassword(string password);

    /// <summary>
    /// Verifies a password against a stored hash and salt.
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="hash">The password hash.</param>
    /// <param name="salt">The password salt.</param>
    /// <returns><see langword="true"/> if the password matches the hash, otherwise <see langword="false"/>.</returns>
    bool VerifyPassword(string password, string hash, string salt);
}
