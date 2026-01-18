namespace Magpie.Models.Result;

/// <summary>
/// Represents a successful result.
/// </summary>
public interface ISuccessResult : IResult
{
}

/// <summary>
/// Represents a successful result with a value.
/// </summary>
public interface ISuccessResult<T> : IResult<T>, ISuccessResult
{
    /// <summary>
    /// The value of the result.
    /// </summary>
    T Value { get; }
}
