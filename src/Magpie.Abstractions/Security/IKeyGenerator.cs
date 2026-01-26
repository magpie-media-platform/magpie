namespace Magpie.Abstractions.Security;

/// <summary>
/// Provides functionality to generate secure keys.
/// </summary>
public interface IKeyGenerator
{
    /// <summary>
    /// Generates a new random key.
    /// </summary>
    /// <param name="prefix">The prefix to use for the key.</param>
    /// <param name="length">The length of the key to generate.</param>
    string GenerateKey(string prefix, int length);
}
