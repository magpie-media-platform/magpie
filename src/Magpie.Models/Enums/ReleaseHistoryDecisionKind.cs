namespace Magpie.Models.Enums;

/// <summary>
/// The kind of decision that the host took for a release.
/// </summary>
public enum ReleaseHistoryDecisionKind
{
    /// <summary>
    /// The host grabbed the release.
    /// </summary>
    Grabbed = 0,

    /// <summary>
    /// The scoring service evaluated the release and rejected it.
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// The release has no decision because nothing evaluated it.
    /// </summary>
    Stalled = 2,
}
