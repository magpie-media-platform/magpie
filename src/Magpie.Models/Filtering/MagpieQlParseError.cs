namespace Magpie.Models.Filtering;

/// <summary>
/// A parse failure for a MagpieQL query.
/// </summary>
public sealed record MagpieQlParseError
{
    /// <summary>
    /// The description of the failure.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The zero-based character position of the failure in the query.
    /// </summary>
    public required int Position { get; set; }

    /// <summary>
    /// The input that the parser expected at the failure position, when known.
    /// </summary>
    public string? Expected { get; set; }
}
