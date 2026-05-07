namespace Magpie.Models.Api;

/// <summary>
/// Response containing a formatted filter query string.
/// </summary>
public sealed record FilterQueryFormatResponse
{
    /// <summary>
    /// The formatted query string.
    /// </summary>
    public required string Query { get; init; }
}
