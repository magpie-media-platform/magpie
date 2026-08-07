namespace Magpie.Models.Enums;

/// <summary>
/// The way a hierarchy level identifies its child nodes for release scope.
/// </summary>
public enum OrdinalKind
{
    /// <summary>
    /// The level has no ordinal and identifies children by name only.
    /// </summary>
    None = 0,

    /// <summary>
    /// The level identifies children by a numeric ordinal.
    /// </summary>
    Number,

    /// <summary>
    /// The level identifies children by a name or an alignment key only.
    /// </summary>
    Name
}
