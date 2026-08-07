namespace Magpie.Sdk.Security;

/// <summary>
/// The protection contract for persisted sensitive values.
/// </summary>
public interface IDataProtectionProvider
{
    /// <summary>
    /// The id of the active protection key.
    /// </summary>
    string ActiveKeyId { get; }

    /// <summary>
    /// Protects a sensitive value.
    /// </summary>
    /// <param name="value">The clear value to protect</param>
    string Protect(string value);

    /// <summary>
    /// Unprotects a sensitive value.
    /// </summary>
    /// <param name="protectedValue">The protected value to return to clear text</param>
    string Unprotect(string protectedValue);

    /// <summary>
    /// Gets the protection key id from a protected value.
    /// </summary>
    /// <param name="protectedValue">The protected value to read</param>
    /// <param name="keyId">The id of the key that protected the value</param>
    bool TryGetKeyId(string protectedValue, out string keyId);
}
