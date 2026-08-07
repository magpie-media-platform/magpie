namespace Magpie.Models.Api;

/// <summary>
/// The status of first-run setup.
/// </summary>
public sealed record SetupStatusResponse
{
    /// <summary>
    /// Whether first-run setup is still necessary.
    /// </summary>
    public required bool IsRequired { get; init; }

    /// <summary>
    /// Whether first-run setup is complete.
    /// </summary>
    public required bool IsComplete { get; init; }

    /// <summary>
    /// Whether the first administrator account is still unclaimed.
    /// </summary>
    public required bool RequiresAdminClaim { get; init; }
}
