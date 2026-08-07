namespace Magpie.Models.Api;

/// <summary>
/// The request to parse a MagpieQL query string into structured criteria.
/// </summary>
public sealed record MagpieQlParseRequest
{
    /// <summary>
    /// The MagpieQL query string to parse.
    /// </summary>
    public required string Query { get; init; }
}
