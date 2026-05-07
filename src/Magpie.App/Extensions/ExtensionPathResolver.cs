using System.IO;

namespace Magpie.App.Extensions;

/// <summary>
/// Resolves the absolute path of the extensions directory.
/// </summary>
public static class ExtensionPathResolver
{
    /// <summary>
    /// The directory name used when no explicit path has been configured.
    /// </summary>
    public const string DefaultDirectoryName = "extensions";

    /// <summary>
    /// Resolves the absolute extensions path, falling back to <c>{contentRoot}/extensions</c>
    /// when no explicit value is configured.
    /// </summary>
    /// <param name="configuredPath">The path read from configuration. May be null or empty.</param>
    /// <param name="contentRoot">The host content root used as a fallback base.</param>
    public static string Resolve(string? configuredPath, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath!
                : Path.GetFullPath(Path.Combine(contentRoot, configuredPath!));
        }

        return Path.GetFullPath(Path.Combine(contentRoot, DefaultDirectoryName));
    }
}
