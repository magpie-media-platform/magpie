namespace Magpie.Models.Enums;

/// <summary>
/// The source of the dynamic options of a setting.
/// </summary>
public enum SettingOptionsSourceKind
{
    /// <summary>
    /// Options from the configured root folders.
    /// </summary>
    RootFolders = 0,

    /// <summary>
    /// Options from the registered content types.
    /// </summary>
    ContentTypes,

    /// <summary>
    /// Options from a JavaScript function.
    /// </summary>
    JSFunction
}
