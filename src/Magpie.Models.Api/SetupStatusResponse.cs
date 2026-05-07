namespace Magpie.Models.Api;

/// <summary>
/// API response for the system setup status.
/// </summary>
public sealed record SetupStatusResponse
{
    /// <summary>
    /// Whether initial setup is required (no admin accounts exist).
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Whether initial setup has been completed (at least one admin account exists).
    /// </summary>
    public bool IsComplete { get; init; }
}
