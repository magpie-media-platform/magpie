namespace Magpie.Models.Enums;

/// <summary>
/// The health status of an external integration.
/// </summary>
public enum IntegrationHealthStatus
{
    /// <summary>
    /// The status is not known.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The integration is healthy and responds.
    /// </summary>
    Healthy = 1,

    /// <summary>
    /// The integration is unhealthy or does not respond.
    /// </summary>
    Unhealthy = 2,

    /// <summary>
    /// The last check is older than the required threshold.
    /// </summary>
    Stale = 3,

    /// <summary>
    /// The integration has temporary or non-fatal errors but is not down.
    /// </summary>
    Degraded = 4,

    /// <summary>
    /// The integration was unhealthy and now succeeds again on the way back to healthy.
    /// </summary>
    Stabilizing = 5
}
