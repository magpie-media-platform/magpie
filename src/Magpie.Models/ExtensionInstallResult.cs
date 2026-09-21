using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// The result of an extension install or upgrade.
/// </summary>
public sealed record ExtensionInstallResult
{
    /// <summary>
    /// The installed or upgraded extension.
    /// </summary>
    public required Extension Extension { get; init; }

    /// <summary>
    /// Whether the host must restart for the change to take effect.
    /// </summary>
    public required bool RestartRequired { get; init; }
}
