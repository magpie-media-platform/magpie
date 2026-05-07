namespace Magpie.Models.Api;

/// <summary>
/// Request to parse a filter query string into criteria.
/// </summary>
public sealed record FilterQueryParseRequest
{
    /// <summary>
    /// The query string to parse.
    /// </summary>
    public required string Query { get; init; }
}
