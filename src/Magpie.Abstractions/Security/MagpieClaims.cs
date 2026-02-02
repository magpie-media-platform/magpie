namespace Magpie.Abstractions.Auth;

/// <summary>
/// Constants for Magpie-specific claim types.
/// </summary>
public static class MagpieClaims
{
    /// <summary>
    /// The claim type for the auth provider type.
    /// </summary>
    public const string AuthProviderExtension = "magpie:auth_provider_ext";
    /// <summary>
    /// The claim type for the auth provider type.
    /// </summary>
    public const string AuthProviderType = "magpie:auth_provider";
}
