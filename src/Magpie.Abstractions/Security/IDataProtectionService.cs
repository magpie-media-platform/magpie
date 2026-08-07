using System.Collections.Generic;

namespace Magpie.Abstractions.Security;

/// <summary>
/// The service contract for the encryption of stored secrets.
/// </summary>
public interface IDataProtectionService
{
    /// <summary>
    /// The id of the key that protects new secrets.
    /// </summary>
    string ActiveKeyId { get; }

    /// <summary>
    /// Protects a set of secret settings.
    /// </summary>
    /// <param name="settings">The plaintext settings to protect</param>
    Dictionary<string, string> Protect(IReadOnlyDictionary<string, string> settings);

    /// <summary>
    /// Unprotects a set of secret settings.
    /// </summary>
    /// <param name="stored">The protected settings to read back</param>
    Dictionary<string, string> Unprotect(IReadOnlyDictionary<string, string> stored);

    /// <summary>
    /// Protects one secret value.
    /// </summary>
    /// <param name="secret">The plaintext value to protect</param>
    string Protect(string secret);

    /// <summary>
    /// Unprotects one secret value.
    /// </summary>
    /// <param name="protectedSecret">The protected value to read back</param>
    string Unprotect(string protectedSecret);

    /// <summary>
    /// Gets the id of the key that protects a set of secret settings.
    /// </summary>
    /// <param name="stored">The protected settings to read</param>
    /// <param name="keyId">The id of the key, set only when the method returns true</param>
    bool TryGetKeyId(IReadOnlyDictionary<string, string> stored, out string keyId);
}
