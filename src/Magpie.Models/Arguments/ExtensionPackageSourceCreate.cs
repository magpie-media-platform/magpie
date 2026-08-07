using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The creation arguments for an extension package source.
/// </summary>
public sealed record ExtensionPackageSourceCreate
{
    /// <summary>
    /// The display name of the source.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The URL of the package source.
    /// </summary>
    public required string SourceUrl { get; set; }

    /// <summary>
    /// Whether the source is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The filter kind that applies to search results from the source.
    /// </summary>
    public ExtensionPackageSourceSearchFilterKind SearchFilterKind { get; set; }

    /// <summary>
    /// The value for the search filter, when the filter kind requires one.
    /// </summary>
    public string? SearchFilterValue { get; set; }

    /// <summary>
    /// The credential scheme for authentication to the source.
    /// </summary>
    public ExtensionPackageSourceCredentialKind CredentialKind { get; set; }

    /// <summary>
    /// The username for basic credentials.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// The plaintext secret for basic credentials.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// The reference to the secret in a secret store.
    /// </summary>
    public string? SecretReference { get; set; }
}
