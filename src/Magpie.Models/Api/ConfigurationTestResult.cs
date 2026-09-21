namespace Magpie.Models.Api;

/// <summary>
/// The result of a settings test for an external service integration.
/// </summary>
public sealed record ConfigurationTestResult
{
    /// <summary>
    /// Whether the test passed.
    /// </summary>
    public required bool Success { get; set; }

    /// <summary>
    /// The message that describes the result of the test.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The kind of failure, or <see cref="ConfigurationFailureKind.None"/> when the test passed.
    /// </summary>
    public ConfigurationFailureKind FailureKind { get; set; } = ConfigurationFailureKind.None;
}
