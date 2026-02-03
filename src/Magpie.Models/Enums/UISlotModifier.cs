namespace Magpie.Models.Enums;

/// <summary>
/// How an extension modifies a UI slot.
/// </summary>
public enum UISlotModifier
{
    /// <summary>
    /// Insert before the default content.
    /// </summary>
    Before = 0,

    /// <summary>
    /// Insert after the default content.
    /// </summary>
    After,

    /// <summary>
    /// Replace the default content entirely.
    /// </summary>
    Replace
}
