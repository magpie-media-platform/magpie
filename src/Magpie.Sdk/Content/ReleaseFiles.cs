using System.IO;
using System.Text.RegularExpressions;

namespace Magpie.Sdk.Content;

/// <summary>
/// The classification of files in a release.
/// </summary>
public static class ReleaseFiles
{
    private static readonly Regex SamplePattern = new(
        @"(?:^|[._-])sample(?:$|[._-])|\[sample\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Gets whether the name of a file marks it as a sample.
    /// </summary>
    /// <param name="filePath">The path of the file</param>
    public static bool IsSample(string filePath) =>
        SamplePattern.IsMatch(Path.GetFileNameWithoutExtension(filePath));
}
