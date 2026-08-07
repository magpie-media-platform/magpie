namespace Magpie.Models.Enums;

/// <summary>
/// The severity of a <c>DiagnosticFinding</c>.
/// </summary>
public enum DiagnosticFindingSeverity
{
    /// <summary>
    /// An observation that needs no action.
    /// </summary>
    Info = 0,

    /// <summary>
    /// A condition that needs review but does not break the system.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// A condition that breaks the system, or is about to break it, and needs attention.
    /// </summary>
    Error = 2
}
