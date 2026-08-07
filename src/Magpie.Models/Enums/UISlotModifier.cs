namespace Magpie.Models.Enums;

/// <summary>
/// The way an extension modifies a UI slot.
/// </summary>
public enum UISlotModifier
{
    /// <summary>
    /// The extension content goes before the default content.
    /// </summary>
    Before = 0,

    /// <summary>
    /// The extension content goes after the default content.
    /// </summary>
    After,

    /// <summary>
    /// The extension content replaces the default content.
    /// </summary>
    Replace
}
