namespace Magpie.Models.Api;

/// <summary>
/// The response with a MagpieQL query string formatted from structured criteria.
/// </summary>
public sealed record MagpieQlFormatResponse
{
    /// <summary>
    /// The formatted MagpieQL query string.
    /// </summary>
    public required string Query { get; init; }
}
