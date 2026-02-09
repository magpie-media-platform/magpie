namespace Magpie.QueryLanguage;

/// <summary>
/// The result of parsing a filter query.
/// </summary>
/// <typeparam name="T">The type of the parsed value.</typeparam>
/// <param name="Value">The parsed value, or null if parsing failed.</param>
/// <param name="Error">The error message, or null if parsing succeeded.</param>
public readonly record struct ParseResult<T>(T? Value, ParseError? Error) where T : class
{
    /// <summary>
    /// Whether parsing was successful.
    /// </summary>
    public bool IsSuccess
    {
        get { return Error is null; }
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The parsed value.</param>
    /// <returns>A successful parse result.</returns>
    public static ParseResult<T> Success(T value)
    {
        return new(value, null);
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The parse error.</param>
    /// <returns>A failed parse result.</returns>
    public static ParseResult<T> Failure(ParseError error)
    {
        return new(null, error);
    }
}
