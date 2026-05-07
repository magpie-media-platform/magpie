namespace Magpie.App.Extensions;

/// <summary>
/// Options that control how the host discovers and loads extensions.
/// </summary>
public sealed class ExtensionLoaderOptions
{
    /// <summary>
    /// The configuration section name used to bind these options.
    /// </summary>
    public const string SectionName = "Magpie";

    /// <summary>
    /// The directory under which extensions are discovered. Each direct subdirectory is
    /// treated as one extension and is scanned for assemblies.
    /// </summary>
    /// <remarks>
    /// When null or empty the loader resolves the path relative to the host content root
    /// using <c>extensions/</c> as the default folder name.
    /// </remarks>
    public string? ExtensionsPath { get; set; }
}
