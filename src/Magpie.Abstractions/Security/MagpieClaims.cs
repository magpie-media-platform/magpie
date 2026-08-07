namespace Magpie.Abstractions.Security;

/// <summary>
/// The claim types that Magpie issues.
/// </summary>
public static class MagpieClaims
{
    /// <summary>
    /// The claim type for the extension that authenticated the user.
    /// </summary>
    public const string AuthProviderExtension = "magpie:auth_provider_ext";

    /// <summary>
    /// The claim type for the auth provider that authenticated the user.
    /// </summary>
    public const string AuthProviderType = "magpie:auth_provider";
}
