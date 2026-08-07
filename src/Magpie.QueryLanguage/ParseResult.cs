namespace Magpie.QueryLanguage;

/// <summary>
/// The result of parsing a filter query.
/// </summary>
/// <typeparam name="T">The type of the parsed value.</typeparam>
/// <param name="Value">The parsed value, or null when the parse failed</param>
/// <param name="Error">The parse error, or null when the parse succeeded</param>
public readonly record struct ParseResult<T>(T? Value, ParseError? Error) where T : class
{
    /// <summary>
    /// Whether the parse succeeded.
    /// </summary>
    public bool IsSuccess
    {
        get { return Error is null; }
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The parsed value</param>
    /// <returns>A successful parse result</returns>
    public static ParseResult<T> Success(T value)
    {
        return new(value, null);
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The parse error</param>
    /// <returns>A failed parse result</returns>
    public static ParseResult<T> Failure(ParseError error)
    {
        return new(null, error);
    }
}
