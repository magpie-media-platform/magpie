namespace Magpie.Models.Enums;

/// <summary>
/// The quality status of the existing files of a content item.
/// </summary>
public enum QualityStatus
{
    /// <summary>
    /// The existing files meet the effective quality-tier target.
    /// </summary>
    Met = 0,

    /// <summary>
    /// The existing files do not meet the effective quality-tier target and an upgrade is wanted.
    /// </summary>
    UpgradeWanted = 1,
}
