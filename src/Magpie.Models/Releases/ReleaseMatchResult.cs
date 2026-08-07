namespace Magpie.Models.Releases;

/// <summary>
/// How well a release matches a content target.
/// </summary>
public enum ReleaseMatchResult
{
    /// <summary>
    /// The release does not match the content target.
    /// </summary>
    NoMatch = 0,

    /// <summary>
    /// The evidence is not sufficient to accept or to reject the match.
    /// </summary>
    Indeterminate = 1,

    /// <summary>
    /// The release matches the content target with confidence.
    /// </summary>
    Confident = 2,

    /// <summary>
    /// The release matches the content target by identity.
    /// </summary>
    Identity = 3,
}
