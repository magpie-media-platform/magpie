namespace Magpie.Models.Enums;

/// <summary>
/// The disposition of a superseded file after a quality upgrade.
/// </summary>
public enum UpgradeFileHandling
{
    /// <summary>
    /// The upgrade import deletes the superseded files.
    /// </summary>
    Replace = 0,

    /// <summary>
    /// The upgrade import keeps the superseded files in place.
    /// </summary>
    KeepBoth = 1,
}
