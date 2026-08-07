namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for diagnostic findings.
/// </summary>
public sealed record DiagnosticFindingQuery
{
    /// <summary>
    /// The diagnostic check id to match, or <c>null</c> for all checks.
    /// </summary>
    public string? DiagnosticCheckId { get; init; }
}
