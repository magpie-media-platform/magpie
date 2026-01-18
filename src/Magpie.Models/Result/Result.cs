namespace Magpie.Models.Result;

/// <summary>
/// Represents the outcome of an operation.
/// </summary>
public abstract record Result(bool IsSuccess) : IResult
{
    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static SuccessResult Success()
    {
        return new();
    }

    /// <summary>
    /// Create a successful result with a value.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <returns>A successful result containing the value.</returns>
    public static SuccessResult<T> Success<T>(T value)
    {
        return new(value);
    }

    /// <summary>
    /// Create a failed result.
    /// </summary>
    /// <param name="reason">The error reason or message.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="subErrorCode">The sub error code.</param>
    /// <returns>A failed result containing the error information.</returns>
    public static FailureResult Fail(string reason, string? errorCode = default, string? subErrorCode = default)
    {
        return new(reason, errorCode, subErrorCode);
    }

    /// <summary>
    /// Create a failed value result.
    /// </summary>
    /// <typeparam name="T">The type of value represented by the result.</typeparam>
    /// <param name="reason">The error reason or message.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="subErrorCode">The sub error code.</param>
    /// <returns>A failed value result containing the error information.</returns>
    public static FailureResult<T> Fail<T>(string reason, string? errorCode = default, string? subErrorCode = default)
    {
        return new(reason, errorCode, subErrorCode);
    }
}

/// <summary>
/// Represents a result that may contain a value.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="IsSuccess">Whether the result is successful.</param>
public abstract record Result<T>(bool IsSuccess) : Result(IsSuccess), IResult<T>;
