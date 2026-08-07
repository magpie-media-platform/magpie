namespace Magpie.Models;

/// <summary>
/// The result of the database configuration step in setup.
/// </summary>
public sealed record DatabaseSetupResult
{
    /// <summary>
    /// The id of the configured database provider.
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// Whether the host must restart for the configuration to take effect.
    /// </summary>
    public required bool RestartRequired { get; set; }
}
