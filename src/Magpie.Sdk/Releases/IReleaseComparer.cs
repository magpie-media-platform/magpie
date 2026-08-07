using Magpie.Models.Data;

namespace Magpie.Sdk.Releases;

/// <summary>
/// A comparer for release order after match result and quality.
/// </summary>
public interface IReleaseComparer
{
    /// <summary>
    /// Determines whether the comparer can order a release.
    /// </summary>
    /// <param name="release">The release to inspect</param>
    bool CanCompare(Release release);

    /// <summary>
    /// Compares two applicable releases.
    /// </summary>
    /// <param name="left">The first release to compare</param>
    /// <param name="right">The second release to compare</param>
    int Compare(Release left, Release right);
}
