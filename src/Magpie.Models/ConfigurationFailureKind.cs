namespace Magpie.Models;

/// <summary>
/// The category of a failure from an integration configuration test.
/// </summary>
public enum ConfigurationFailureKind
{
    /// <summary>
    /// No failure category is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The failure can clear without a configuration change.
    /// </summary>
    Transient,

    /// <summary>
    /// The upstream service rejected the test because of a rate limit.
    /// </summary>
    RateLimited,

    /// <summary>
    /// The upstream service does not hold the requested resource.
    /// </summary>
    NotFound,

    /// <summary>
    /// Authentication or authorization failed.
    /// </summary>
    Auth,

    /// <summary>
    /// The failure continues until the configuration or the upstream service changes.
    /// </summary>
    Fatal
}
