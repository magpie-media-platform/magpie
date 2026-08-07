namespace Magpie.Models.Enums;

/// <summary>
/// The credential storage mode of an extension package source.
/// </summary>
public enum ExtensionPackageSourceCredentialKind
{
    /// <summary>
    /// The source needs no credentials.
    /// </summary>
    None = 0,

    /// <summary>
    /// The source uses HTTP Basic authentication with an encrypted secret.
    /// </summary>
    Basic = 1,

    /// <summary>
    /// The source points to a secret outside the database.
    /// </summary>
    SecretReference = 2
}
