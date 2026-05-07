namespace Magpie.Models.Api;

/// <summary>
/// Describes an authentication provider available on the login page.
/// </summary>
public sealed record LoginProviderResponse
{
    /// <summary>
    /// Unique identifier for the provider.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the provider.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Provider type. One of: local, oidc, ldap, saml.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether this provider collects a username and password.
    /// </summary>
    public required bool UsesCredentials { get; init; }

    /// <summary>
    /// Optional icon hint used by the UI to select a brand icon.
    /// </summary>
    public string? IconHint { get; init; }
}
