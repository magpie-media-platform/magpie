namespace Magpie.Models.Api;

/// <summary>
/// Health check response.
/// </summary>
public sealed record HealthResponse
{
    /// <summary>
    /// The health status.
    /// </summary>
    public required string Status { get; init; }
}
