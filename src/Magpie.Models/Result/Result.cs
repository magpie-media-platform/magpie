namespace Magpie.Models.Result;

/// <summary>
/// Represents the outcome of an operation.
/// </summary>
/// <param name="IsSuccess"><inheritdoc cref="IResult.IsSuccess" path="/summary"/></param>
public abstract record Result(bool IsSuccess) : IResult
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static SuccessResult Success()
    {
        return new();
    }

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <returns>A successful result containing the value.</returns>
    public static SuccessResult<T> Success<T>(T value)
    {
        return new(value);
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="reason"><inheritdoc cref="IFailureResult.Reason" path="/summary"/></param>
    /// <param name="errorCode"><inheritdoc cref="IFailureResult.ErrorCode" path="/summary"/></param>
    /// <param name="subErrorCode"><inheritdoc cref="IFailureResult.SubErrorCode" path="/summary"/></param>
    /// <returns>A failed result containing the error information.</returns>
    public static FailureResult Fail(string reason, string? errorCode = default, string? subErrorCode = default)
    {
        return new(reason, errorCode, subErrorCode);
    }

    /// <summary>
    /// Creates a failed value result.
    /// </summary>
    /// <typeparam name="T">The type of value represented by the result.</typeparam>
    /// <param name="reason"><inheritdoc cref="IFailureResult.Reason" path="/summary"/></param>
    /// <param name="errorCode"><inheritdoc cref="IFailureResult.ErrorCode" path="/summary"/></param>
    /// <param name="subErrorCode"><inheritdoc cref="IFailureResult.SubErrorCode" path="/summary"/></param>
    /// <returns>A failed typed result containing the error information.</returns>
    public static FailureResult<T> Fail<T>(string reason, string? errorCode = default, string? subErrorCode = default)
    {
        return new(reason, errorCode, subErrorCode);
    }
}

/// <summary>
/// Represents a result that may contain a value.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="IsSuccess"><inheritdoc cref="IResult.IsSuccess" path="/summary"/></param>
public abstract record Result<T>(bool IsSuccess) : Result(IsSuccess), IResult<T>;
