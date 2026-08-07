namespace Magpie.Models.Api;

/// <summary>
/// The request to install an extension from a configured package source.
/// </summary>
public sealed record InstallExtensionRequest
{
    /// <summary>
    /// The id of the package to install.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// The version to install, or <see langword="null"/> for the latest version.
    /// </summary>
    public string? Version { get; init; }
}
