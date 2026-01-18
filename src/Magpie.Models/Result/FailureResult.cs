namespace Magpie.Models.Result;

/// <summary>
/// Represents a failed result.
/// </summary>
/// <param name="Reason"><inheritdoc cref="IFailureResult.Reason" path="/summary"/></param>
/// <param name="ErrorCode"><inheritdoc cref="IFailureResult.ErrorCode" path="/summary"/></param>
/// <param name="SubErrorCode"><inheritdoc cref="IFailureResult.SubErrorCode" path="/summary"/></param>
public sealed record FailureResult(
    string Reason,
    string? ErrorCode = default,
    string? SubErrorCode = default) : Result(false), IFailureResult;

/// <summary>
/// Represents a failed value result.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="Reason"><inheritdoc cref="IFailureResult.Reason" path="/summary"/></param>
/// <param name="ErrorCode"><inheritdoc cref="IFailureResult.ErrorCode" path="/summary"/></param>
/// <param name="SubErrorCode"><inheritdoc cref="IFailureResult.SubErrorCode" path="/summary"/></param>
public sealed record FailureResult<T>(
    string Reason,
    string? ErrorCode = default,
    string? SubErrorCode = default) : Result<T>(false), IFailureResult<T>;
