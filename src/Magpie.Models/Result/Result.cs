using System.Threading.Tasks;

namespace Magpie.Models.Result;

/// <summary>
/// Represents the outcome of an operation.
/// </summary>
public static class Result
{

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <returns>A successful result containing the value.</returns>
    public static Result<T> Success<T>(T value)
    {
        return new SuccessResult<T>(value);
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result<Unit> Success()
    {
        return Success(Unit.Default);
    }

    /// <summary>
    /// Creates a successful completed result task containing a value.
    /// </summary>
    public static Task<Result<T>> SuccessAsync<T>(T value)
    {
        return Task.FromResult(Success(value));
    }

    /// <summary>
    /// Creates a successful completed result task.
    /// </summary>
    public static Task<Result<Unit>> SuccessAsync()
    {
        return Task.FromResult(Success());
    }

    /// <summary>
    /// Creates a failed value result.
    /// </summary>
    /// <typeparam name="T">The type of value represented by the result.</typeparam>
    /// <param name="reason"><inheritdoc cref="FailureResult{T}.Reason" path="/summary"/></param>
    /// <param name="errorCode"><inheritdoc cref="FailureResult{T}.ErrorCode" path="/summary"/></param>
    /// <param name="subErrorCode"><inheritdoc cref="FailureResult{T}.SubErrorCode" path="/summary"/></param>
    /// <returns>A failed typed result containing the error information.</returns>
    public static Result<T> Fail<T>(string reason, string? errorCode = default, string? subErrorCode = default)
    {
        return new FailureResult<T>(reason, errorCode, subErrorCode);
    }

    /// <summary>
    /// Creates a failed completed result task containing a value.
    /// </summary>
    /// <param name="reason"><inheritdoc cref="FailureResult{T}.Reason" path="/summary"/></param>
    /// <param name="errorCode"><inheritdoc cref="FailureResult{T}.ErrorCode" path="/summary"/></param>
    /// <param name="subErrorCode"><inheritdoc cref="FailureResult{T}.SubErrorCode" path="/summary"/></param>
    /// <returns>A failed result task containing the error information.</returns>
    public static Task<Result<T>> FailAsync<T>(
        string reason,
        string? errorCode = default,
        string? subErrorCode = default)
    {
        return Task.FromResult((Result<T>)Fail<T>(reason, errorCode, subErrorCode));
    }
}

/// <summary>
/// Represents a result that may contain a value.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="IsSuccess">Whether the result is successful.</param>
public abstract record Result<T>(bool IsSuccess);
