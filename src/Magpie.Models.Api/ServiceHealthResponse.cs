namespace Magpie.Models.Api;

/// <summary>
/// Health check response for an individual service.
/// </summary>
public sealed record ServiceHealthResponse
{
    /// <summary>
    /// The service name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The health status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Optional descriptive message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional response time in milliseconds.
    /// </summary>
    public long? ResponseTimeMs { get; init; }
}
