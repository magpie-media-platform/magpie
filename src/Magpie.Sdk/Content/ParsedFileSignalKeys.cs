namespace Magpie.Sdk.Content;

/// <summary>
/// The signal keys that a content type sets for a parsed file.
/// </summary>
public static class ParsedFileSignalKeys
{
    /// <summary>
    /// The signal key of the parsed title.
    /// </summary>
    public const string Title = "title";

    /// <summary>
    /// The signal key of the parsed release year.
    /// </summary>
    public const string Year = "year";

    /// <summary>
    /// The signal key of the parsed release month.
    /// </summary>
    public const string Month = "month";

    /// <summary>
    /// The signal key of the parsed release day.
    /// </summary>
    public const string Day = "day";

    /// <summary>
    /// The signal key of the title token for metadata search.
    /// </summary>
    public const string TitleToken = "titleToken";

    /// <summary>
    /// The signal key of the title of the root-level item of a multi-level content type.
    /// </summary>
    public const string RootTitle = "rootTitle";
}
