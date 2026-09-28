namespace Magpie.Models.Enums;

/// <summary>
/// The status of a release in the scope of a content hierarchy item.
/// </summary>
public enum ReleaseScopeStatus
{
    /// <summary>
    /// The release matches the content hierarchy item.
    /// </summary>
    Matched = 0,

    /// <summary>
    /// A user unmatched the release from the content hierarchy item, so that automation does not choose it.
    /// </summary>
    Unmatched = 1,
}
