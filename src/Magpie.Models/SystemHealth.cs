namespace Magpie.Models;

/// <summary>
/// A liveness summary for the Magpie host.
/// </summary>
public sealed record SystemHealth
{
    /// <summary>
    /// The health status text.
    /// </summary>
    public required string Status { get; init; }
}
