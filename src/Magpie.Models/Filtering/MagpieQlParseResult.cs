namespace Magpie.Models.Filtering;

/// <summary>
/// The result of a MagpieQL query parse into structured criteria.
/// </summary>
/// <typeparam name="T">The structured criteria type</typeparam>
public sealed record MagpieQlParseResult<T>
    where T : class
{
    /// <summary>
    /// The parsed criteria, or <see langword="null"/> when the parse failed.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// The parse error, or <see langword="null"/> when the parse succeeded.
    /// </summary>
    public MagpieQlParseError? Error { get; init; }

    /// <summary>
    /// Whether the parse succeeded.
    /// </summary>
    public bool IsSuccess => Error is null;
}
