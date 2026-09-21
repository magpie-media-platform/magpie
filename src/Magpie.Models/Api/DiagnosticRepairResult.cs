namespace Magpie.Models.Api;

/// <summary>
/// The outcome of an <c>IDiagnosticRepair</c> run against a finding.
/// </summary>
public sealed record DiagnosticRepairResult
{
    /// <summary>
    /// Whether the repair succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The description of what the repair did, or the reason why it did not run.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Creates a successful result with the given message.
    /// </summary>
    /// <param name="message">The description of what the repair did</param>
    public static DiagnosticRepairResult Succeeded(string message) => new() { Success = true, Message = message };

    /// <summary>
    /// Creates a failed result with the given message.
    /// </summary>
    /// <param name="message">The reason why the repair did not run or did not complete</param>
    public static DiagnosticRepairResult Failed(string message) => new() { Success = false, Message = message };
}
