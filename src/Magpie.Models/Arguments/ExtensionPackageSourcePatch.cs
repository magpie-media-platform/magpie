using System;

using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an extension package source.
/// </summary>
public sealed record ExtensionPackageSourcePatch
{
    /// <summary>
    /// The id of the package source to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the display name of the source.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the source URL.
    /// </summary>
    public ScalarPatch<string>? SourceUrl { get; init; }

    /// <summary>
    /// The patch for whether the source is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the kind of search filter.
    /// </summary>
    public ScalarPatch<ExtensionPackageSourceSearchFilterKind>? SearchFilterKind { get; init; }

    /// <summary>
    /// The patch for the search filter value.
    /// </summary>
    public ScalarPatch<string?>? SearchFilterValue { get; init; }

    /// <summary>
    /// The patch for the kind of credential.
    /// </summary>
    public ScalarPatch<ExtensionPackageSourceCredentialKind>? CredentialKind { get; init; }

    /// <summary>
    /// The patch for the user name.
    /// </summary>
    public ScalarPatch<string?>? Username { get; init; }

    /// <summary>
    /// The patch for the encrypted secret.
    /// </summary>
    public ScalarPatch<string?>? EncryptedSecret { get; init; }

    /// <summary>
    /// The patch for the secret reference.
    /// </summary>
    public ScalarPatch<string?>? SecretReference { get; init; }
}
