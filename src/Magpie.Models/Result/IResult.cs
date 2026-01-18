namespace Magpie.Models.Result;

/// <summary>
/// Represents the outcome of an operation.
/// </summary>
public interface IResult
{
    /// <summary>
    /// Whether the result is successful.
    /// </summary>
    bool IsSuccess { get; }
}

/// <summary>
/// Represents a typed result.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
public interface IResult<T> : IResult
{
}
