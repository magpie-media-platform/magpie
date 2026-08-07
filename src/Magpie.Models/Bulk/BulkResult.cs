namespace Magpie.Models.Bulk;

/// <summary>
/// The result of a bulk operation.
/// </summary>
public sealed record BulkResult
{
    /// <summary>
    /// The number of affected items.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>
    /// The error message, or <see langword="null"/> when the operation succeeds.
    /// </summary>
    public string? Error { get; init; }
}
