using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of an extension package source.
/// </summary>
public sealed record ExtensionPackageSourceResponse
{
    /// <summary>
    /// The id of the package source.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The name of the package source.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The URL of the NuGet source.
    /// </summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Whether the package source is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The kind of the search filter.
    /// </summary>
    public ExtensionPackageSourceSearchFilterKind SearchFilterKind { get; init; }

    /// <summary>
    /// The value of the search filter.
    /// </summary>
    public string? SearchFilterValue { get; init; }

    /// <summary>
    /// The kind of the credential for the source.
    /// </summary>
    public ExtensionPackageSourceCredentialKind CredentialKind { get; init; }

    /// <summary>
    /// The username for basic authentication.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// The reference to an externally managed secret.
    /// </summary>
    public string? SecretReference { get; init; }

    /// <summary>
    /// Whether the source has a stored secret.
    /// </summary>
    public bool HasSecret { get; init; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The UTC time when the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }
}
