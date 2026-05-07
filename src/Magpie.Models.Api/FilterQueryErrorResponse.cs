using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// Response returned when a filter query fails to parse.
/// </summary>
public sealed record FilterQueryErrorResponse
{
    /// <summary>
    /// The error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The position in the query string where the error occurred.
    /// </summary>
    public required int Position { get; init; }

    /// <summary>
    /// The tokens or expressions expected at the error position.
    /// </summary>
    public required IReadOnlyList<string> Expected { get; init; }
}
