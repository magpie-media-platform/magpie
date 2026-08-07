using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A configured NuGet package source for extension discovery and installation.
/// </summary>
public record ExtensionPackageSource
{
    /// <summary>
    /// The unique id of the extension package source.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The display name of the package source.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The NuGet source URL, usually a v3 service index.
    /// </summary>
    public required string SourceUrl { get; set; }

    /// <summary>
    /// Whether the package source is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The kind of the search filter.
    /// </summary>
    public ExtensionPackageSourceSearchFilterKind SearchFilterKind { get; set; }

    /// <summary>
    /// The value of the search filter.
    /// </summary>
    public string? SearchFilterValue { get; set; }

    /// <summary>
    /// The kind of the credential.
    /// </summary>
    public ExtensionPackageSourceCredentialKind CredentialKind { get; set; }

    /// <summary>
    /// The user name for the package source.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// The secret for basic authentication, which repositories keep encrypted.
    /// </summary>
    public string? EncryptedSecret { get; set; }

    /// <summary>
    /// The reference to an externally managed secret.
    /// </summary>
    public string? SecretReference { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
