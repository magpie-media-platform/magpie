namespace Magpie.Core.Extensions;

/// <summary>
/// Options that tell <see cref="ExtensionInstaller"/> where on disk to stage extensions.
/// </summary>
/// <remarks>
/// Registered as a singleton at startup with the resolved absolute path of the host's
/// extensions directory. Kept in <c>Magpie.Core</c> so the installer does not need to
/// depend on <c>Magpie.App</c>.
/// </remarks>
public sealed class ExtensionInstallerOptions
{
    /// <summary>
    /// The root directory under which each installed extension lives in its own subdirectory.
    /// </summary>
    public required string ExtensionsRoot { get; init; }
}
