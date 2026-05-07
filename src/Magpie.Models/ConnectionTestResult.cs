namespace Magpie.Models;

/// <summary>
/// The result of a connection test to an external service.
/// </summary>
public sealed record ConnectionTestResult
{
    /// <summary>
    /// Whether the connection test succeeded.
    /// </summary>
    public required bool Success { get; init; }
    /// <summary>
    /// A human-readable message describing the result.
    /// </summary>
    public required string Message { get; init; }
}
